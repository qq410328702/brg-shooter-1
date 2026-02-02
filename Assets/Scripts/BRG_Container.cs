using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// BRG容器类 - 使用BatchRendererGroup技术高效渲染大量实例
///
/// 功能说明：
/// 此类负责管理地面格子和碎片的GPU渲染数据。
/// 两者使用相同的GPU数据布局：
///     - obj2world矩阵（3 * float4）- 物体到世界空间的变换矩阵
///     - world2obj矩阵（3 * float4）- 世界到物体空间的逆变换矩阵
///     - color颜色（1 * float4）- 实例颜色
///
/// 每个网格实例占用7个float4（112字节）。
///
/// 重要提示：数据以SoA（Structure of Arrays）格式存储，
/// 即所有矩阵连续存储，然后是所有颜色连续存储。
///
/// 技术背景：
/// BatchRendererGroup (BRG) 是Unity提供的底层渲染API，
/// 允许开发者直接控制GPU实例化渲染，实现极高的渲染性能。
/// </summary>
public unsafe class BRG_Container
{
    // ==================== 平台相关配置 ====================

    /// <summary>
    /// 检查是否使用常量缓冲区（UBO）模式
    /// 在GLES平台上，BRG原始缓冲区是常量缓冲区（UBO）
    /// 在其他平台上使用SSBO（Shader Storage Buffer Object）
    /// </summary>
    private bool UseConstantBuffer => BatchRendererGroup.BufferTarget == BatchBufferTarget.ConstantBuffer;

    /// <summary>
    /// 是否投射阴影
    /// </summary>
    private bool m_castShadows;

    // ==================== 实例管理变量 ====================

    /// <summary>
    /// 此容器支持的最大实例数量
    /// </summary>
    private int m_maxInstances;

    /// <summary>
    /// 当前实例数量
    /// </summary>
    private int m_instanceCount;

    /// <summary>
    /// BRG原始窗口大小（字节对齐后）
    /// </summary>
    private int m_alignedGPUWindowSize;

    /// <summary>
    /// 每个窗口的最大实例数
    /// 在UBO模式下受常量缓冲区大小限制
    /// </summary>
    private int m_maxInstancePerWindow;

    /// <summary>
    /// 窗口数量
    /// SSBO模式下为1，UBO模式下可能为多个
    /// </summary>
    private int m_windowCount;

    /// <summary>
    /// GPU原始缓冲区总大小（字节）
    /// </summary>
    private int m_totalGpuBufferSize;

    /// <summary>
    /// GPU原始缓冲区的系统内存副本
    /// 用于CPU端更新数据后上传到GPU
    /// </summary>
    private NativeArray<float4> m_sysmemBuffer;

    /// <summary>
    /// 是否已初始化
    /// </summary>
    private bool m_initialized;

    /// <summary>
    /// 单个实例的大小（字节）
    /// </summary>
    private int m_instanceSize;

    /// <summary>
    /// 批次ID数组，每个窗口对应一个批次ID
    /// </summary>
    private BatchID[] m_batchIDs;

    /// <summary>
    /// 材质ID
    /// </summary>
    private BatchMaterialID m_materialID;

    /// <summary>
    /// 网格ID
    /// </summary>
    private BatchMeshID m_meshID;

    /// <summary>
    /// BatchRendererGroup对象实例
    /// </summary>
    private BatchRendererGroup m_BatchRendererGroup;

    /// <summary>
    /// GPU持久化实例数据缓冲区
    /// 可以是SSBO或UBO，取决于平台
    /// </summary>
    private GraphicsBuffer m_GPUPersistentInstanceData;

    /// <summary>
    /// 初始化BRG容器，创建BRG对象并分配缓冲区
    /// </summary>
    /// <param name="mesh">要渲染的网格</param>
    /// <param name="mat">要使用的材质</param>
    /// <param name="maxInstances">最大实例数量</param>
    /// <param name="instanceSize">单个实例的数据大小（字节）</param>
    /// <param name="castShadows">是否投射阴影</param>
    /// <returns>初始化是否成功</returns>
    public bool Init(Mesh mesh, Material mat, int maxInstances, int instanceSize, bool castShadows)
    {
        // 创建BRG对象，指定剔除回调函数
        m_BatchRendererGroup = new BatchRendererGroup(this.OnPerformCulling, IntPtr.Zero);

        m_instanceSize = instanceSize;
        m_instanceCount = 0;
        m_maxInstances = maxInstances;
        m_castShadows = castShadows;

        // BRG使用大型GPU缓冲区
        // 在大多数平台上是RAW缓冲区（SSBO），在GLES上是常量缓冲区（UBO）
        // 对于常量缓冲区，需要将其分割成多个"窗口"，每个窗口大小为GetConstantBufferMaxWindowSize()字节
        if (UseConstantBuffer)
        {
            // UBO模式：获取常量缓冲区最大窗口大小
            m_alignedGPUWindowSize = BatchRendererGroup.GetConstantBufferMaxWindowSize();
            // 计算每个窗口能容纳的最大实例数
            m_maxInstancePerWindow = m_alignedGPUWindowSize / instanceSize;
            // 计算需要的窗口数量（向上取整）
            m_windowCount = (m_maxInstances + m_maxInstancePerWindow - 1) / m_maxInstancePerWindow;
            // 计算总缓冲区大小
            m_totalGpuBufferSize = m_windowCount * m_alignedGPUWindowSize;
            // 创建常量缓冲区
            m_GPUPersistentInstanceData = new GraphicsBuffer(GraphicsBuffer.Target.Constant, m_totalGpuBufferSize / 16, 16);
        }
        else
        {
            // SSBO模式：计算对齐后的窗口大小（16字节对齐）
            m_alignedGPUWindowSize = (m_maxInstances * instanceSize + 15) & (-16);
            m_maxInstancePerWindow = maxInstances;
            m_windowCount = 1;  // SSBO模式只需要一个窗口
            m_totalGpuBufferSize = m_windowCount * m_alignedGPUWindowSize;
            // 创建原始缓冲区
            m_GPUPersistentInstanceData = new GraphicsBuffer(GraphicsBuffer.Target.Raw, m_totalGpuBufferSize / 4, 4);
        }

        // 在示例游戏中，我们处理3个实例化属性：obj2world矩阵、world2obj矩阵和baseColor颜色
        var batchMetadata = new NativeArray<MetadataValue>(3, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

        // 获取Shader属性ID
        int objectToWorldID = Shader.PropertyToID("unity_ObjectToWorld");  // 物体到世界矩阵
        int worldToObjectID = Shader.PropertyToID("unity_WorldToObject");  // 世界到物体矩阵
        int colorID = Shader.PropertyToID("_BaseColor");                   // 基础颜色

        // 创建GPU原始缓冲区的系统内存副本
        m_sysmemBuffer = new NativeArray<float4>(m_totalGpuBufferSize / 16, Allocator.Persistent, NativeArrayOptions.ClearMemory);

        // 为大型BRG原始缓冲区中的每个"窗口"注册一个批次
        m_batchIDs = new BatchID[m_windowCount];
        for (int b = 0; b < m_windowCount; b++)
        {
            // 设置元数据：指定每个属性在缓冲区中的偏移量
            batchMetadata[0] = CreateMetadataValue(objectToWorldID, 0, true);                                    // obj2world矩阵起始位置
            batchMetadata[1] = CreateMetadataValue(worldToObjectID, m_maxInstancePerWindow * 3 * 16, true);      // world2obj矩阵起始位置
            batchMetadata[2] = CreateMetadataValue(colorID, m_maxInstancePerWindow * 3 * 2 * 16, true);          // 颜色起始位置

            // 计算当前窗口在缓冲区中的偏移量
            int offset = b * m_alignedGPUWindowSize;
            // 添加批次
            m_batchIDs[b] = m_BatchRendererGroup.AddBatch(batchMetadata, m_GPUPersistentInstanceData.bufferHandle, (uint)offset, UseConstantBuffer ? (uint)m_alignedGPUWindowSize : 0);
        }

        // 释放临时元数据数组
        batchMetadata.Dispose();

        // 设置非常大的包围盒，确保BRG永远不会被视锥体剔除
        UnityEngine.Bounds bounds = new Bounds(new Vector3(0, 0, 0), new Vector3(1048576.0f, 1048576.0f, 1048576.0f));
        m_BatchRendererGroup.SetGlobalBounds(bounds);

        // 注册网格和材质
        if (mesh) m_meshID = m_BatchRendererGroup.RegisterMesh(mesh);
        if (mat) m_materialID = m_BatchRendererGroup.RegisterMaterial(mat);

        m_initialized = true;
        return true;
    }

    /// <summary>
    /// 根据实例数量上传最小必要的GPU数据
    /// 由于使用SoA布局且管理3个BRG属性（2个矩阵和1个颜色），
    /// 最后一个窗口可能需要最多3次SetData调用
    /// </summary>
    /// <param name="instanceCount">要上传的实例数量</param>
    /// <returns>上传是否成功</returns>
    [BurstCompile]
    public bool UploadGpuData(int instanceCount)
    {
        // 检查实例数量是否超出最大限制
        if ((uint)instanceCount > (uint)m_maxInstances)
            return false;

        m_instanceCount = instanceCount;
        // 计算完整窗口的数量
        int completeWindows = m_instanceCount / m_maxInstancePerWindow;

        // 一次性更新所有完整窗口的数据
        if (completeWindows > 0)
        {
            int sizeInFloat4 = (completeWindows * m_alignedGPUWindowSize) / 16;
            m_GPUPersistentInstanceData.SetData(m_sysmemBuffer, 0, 0, sizeInFloat4);
        }

        // 然后上传最后一个（不完整）窗口的数据
        int lastBatchId = completeWindows;
        int itemInLastBatch = m_instanceCount - m_maxInstancePerWindow * completeWindows;

        if (itemInLastBatch > 0)
        {
            // 计算最后一个窗口在缓冲区中的偏移量
            int windowOffsetInFloat4 = (lastBatchId * m_alignedGPUWindowSize) / 16;
            int offsetMat1 = windowOffsetInFloat4 + m_maxInstancePerWindow * 0;      // obj2world矩阵偏移
            int offsetMat2 = windowOffsetInFloat4 + m_maxInstancePerWindow * 3;      // world2obj矩阵偏移
            int offsetColor = windowOffsetInFloat4 + m_maxInstancePerWindow * 3 * 2; // 颜色偏移

            // 分别上传三个属性的数据
            m_GPUPersistentInstanceData.SetData(m_sysmemBuffer, offsetMat1, offsetMat1, itemInLastBatch * 3);     // 3个float4用于obj2world
            m_GPUPersistentInstanceData.SetData(m_sysmemBuffer, offsetMat2, offsetMat2, itemInLastBatch * 3);     // 3个float4用于world2obj
            m_GPUPersistentInstanceData.SetData(m_sysmemBuffer, offsetColor, offsetColor, itemInLastBatch * 1);   // 1个float4用于颜色
        }

        return true;
    }

    /// <summary>
    /// 释放所有已分配的缓冲区和资源
    /// </summary>
    public void Shutdown()
    {
        if (m_initialized)
        {
            // 移除所有批次
            for (uint b = 0; b < m_windowCount; b++)
                m_BatchRendererGroup.RemoveBatch(m_batchIDs[b]);

            // 注销材质和网格
            m_BatchRendererGroup.UnregisterMaterial(m_materialID);
            m_BatchRendererGroup.UnregisterMesh(m_meshID);

            // 释放BRG对象
            m_BatchRendererGroup.Dispose();
            // 释放GPU缓冲区
            m_GPUPersistentInstanceData.Dispose();
            // 释放系统内存缓冲区
            m_sysmemBuffer.Dispose();
        }
    }

    /// <summary>
    /// 获取系统内存缓冲区和窗口大小
    /// 供BRG_Background和BRG_Debris填充新内容
    /// </summary>
    /// <param name="totalSize">输出：缓冲区总大小</param>
    /// <param name="alignedWindowSize">输出：对齐后的窗口大小</param>
    /// <returns>系统内存缓冲区的NativeArray引用</returns>
    public NativeArray<float4> GetSysmemBuffer(out int totalSize, out int alignedWindowSize)
    {
        totalSize = m_totalGpuBufferSize;
        alignedWindowSize = m_alignedGPUWindowSize;
        return m_sysmemBuffer;
    }

    /// <summary>
    /// 辅助函数：创建32位元数据值
    /// 第31位表示属性是否为每个实例不同的值
    /// </summary>
    /// <param name="nameID">Shader属性名称ID</param>
    /// <param name="gpuOffset">在GPU缓冲区中的偏移量</param>
    /// <param name="isPerInstance">是否为每实例属性</param>
    /// <returns>元数据值结构</returns>
    static MetadataValue CreateMetadataValue(int nameID, int gpuOffset, bool isPerInstance)
    {
        const uint kIsPerInstanceBit = 0x80000000;  // 第31位标记
        return new MetadataValue
        {
            NameID = nameID,
            Value = (uint)gpuOffset | (isPerInstance ? (kIsPerInstanceBit) : 0),
        };
    }

    /// <summary>
    /// 辅助函数：在BRG回调函数中分配临时缓冲区
    /// 使用TempJob分配器，会在作业完成后自动释放
    /// </summary>
    /// <typeparam name="T">要分配的非托管类型</typeparam>
    /// <param name="count">元素数量</param>
    /// <returns>分配的内存指针</returns>
    private static T* Malloc<T>(uint count) where T : unmanaged
    {
        return (T*)UnsafeUtility.Malloc(
            UnsafeUtility.SizeOf<T>() * count,
            UnsafeUtility.AlignOf<T>(),
            Allocator.TempJob);
    }

    /// <summary>
    /// BRG主入口点 - 每帧调用的剔除回调函数
    /// 在此示例中不使用BatchCullingContext进行剔除
    /// 此回调负责填充cullingOutput，包含渲染所有项目所需的绘制命令
    /// </summary>
    /// <param name="rendererGroup">BRG渲染器组</param>
    /// <param name="cullingContext">剔除上下文（本示例未使用）</param>
    /// <param name="cullingOutput">剔除输出，需要填充绘制命令</param>
    /// <param name="userContext">用户上下文指针</param>
    /// <returns>作业句柄（本示例返回空句柄）</returns>
    [BurstCompile]
    public JobHandle OnPerformCulling(BatchRendererGroup rendererGroup, BatchCullingContext cullingContext, BatchCullingOutput cullingOutput, IntPtr userContext)
    {
        if (m_initialized)
        {
            BatchCullingOutputDrawCommands drawCommands = new BatchCullingOutputDrawCommands();

            // 计算UBO模式下需要的绘制命令数量（每个窗口一个绘制命令）
            int drawCommandCount = (m_instanceCount + m_maxInstancePerWindow - 1) / m_maxInstancePerWindow;
            int maxInstancePerDrawCommand = m_maxInstancePerWindow;
            drawCommands.drawCommandCount = drawCommandCount;

            // 分配单个BatchDrawRange（所有绘制命令都引用这个DrawRange）
            drawCommands.drawRangeCount = 1;
            drawCommands.drawRanges = Malloc<BatchDrawRange>(1);
            drawCommands.drawRanges[0] = new BatchDrawRange
            {
                drawCommandsBegin = 0,
                drawCommandsCount = (uint)drawCommandCount,
                filterSettings = new BatchFilterSettings
                {
                    renderingLayerMask = 1,                                                              // 渲染层掩码
                    layer = 0,                                                                           // 层级
                    motionMode = MotionVectorGenerationMode.Camera,                                      // 运动向量模式
                    shadowCastingMode = m_castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,    // 阴影投射模式
                    receiveShadows = true,                                                               // 接收阴影
                    staticShadowCaster = false,                                                          // 非静态阴影投射者
                    allDepthSorted = false                                                               // 不进行深度排序
                }
            };

            if (drawCommands.drawCommandCount > 0)
            {
                // 由于不需要剔除，可见性整数数组始终为{0,1,2,3,...}
                // 只需分配maxInstancePerDrawCommand大小并填充
                int visibilityArraySize = maxInstancePerDrawCommand;
                if (m_instanceCount < visibilityArraySize)
                    visibilityArraySize = m_instanceCount;

                drawCommands.visibleInstances = Malloc<int>((uint)visibilityArraySize);

                // 由于不需要视锥体剔除，用{0,1,2,3,...}填充可见性数组
                for (int i = 0; i < visibilityArraySize; i++)
                    drawCommands.visibleInstances[i] = i;

                // 分配BatchDrawCommand数组（drawCommandCount个条目）
                // 在SSBO模式下，drawCommandCount只有1
                drawCommands.drawCommands = Malloc<BatchDrawCommand>((uint)drawCommandCount);
                int left = m_instanceCount;  // 剩余要处理的实例数
                for (int b = 0; b < drawCommandCount; b++)
                {
                    // 计算当前批次的实例数量
                    int inBatchCount = left > maxInstancePerDrawCommand ? maxInstancePerDrawCommand : left;
                    drawCommands.drawCommands[b] = new BatchDrawCommand
                    {
                        visibleOffset = (uint)0,                // 所有绘制命令使用相同的{0,1,2,3...}可见性数组
                        visibleCount = (uint)inBatchCount,      // 可见实例数量
                        batchID = m_batchIDs[b],                // 批次ID
                        materialID = m_materialID,              // 材质ID
                        meshID = m_meshID,                      // 网格ID
                        submeshIndex = 0,                       // 子网格索引
                        splitVisibilityMask = 0xff,             // 分割可见性掩码
                        flags = BatchDrawCommandFlags.None,     // 无特殊标志
                        sortingPosition = 0                     // 排序位置
                    };
                    left -= inBatchCount;
                }
            }

            // 设置剔除输出
            cullingOutput.drawCommands[0] = drawCommands;
            drawCommands.instanceSortingPositions = null;       // 不使用实例排序位置
            drawCommands.instanceSortingPositionFloatCount = 0;
        }

        // 返回空的作业句柄（本示例不使用作业系统进行剔除）
        return new JobHandle();
    }
}
