using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace UnityEngine.Experimental.Rendering
{
    /// <summary>
    /// SRP Batcher 性能分析器
    /// 用于监控和显示 SRP Batcher 的渲染性能数据
    /// 按 F8 键切换显示/隐藏性能统计面板
    /// 按 F9 键切换 SRP Batcher 开关状态
    /// </summary>
    public class SRPBatcherProfiler : MonoBehaviour
    {
        // 是否启用性能统计显示
        public bool m_Enable = false;

        // 统计数据刷新间隔（秒），每秒刷新一次
        private const float kAverageStatDuration = 1.0f;

        // 帧计数器，用于计算平均值
        private int m_frameCount;

        // 累计时间增量
        private float m_AccDeltaTime;

        // 显示的统计信息文本
        private string m_statsLabel;

        // GUI 文本样式
        private GUIStyle m_style;

        // 记录上一次 SRP Batcher 的启用状态，用于检测状态变化
        private bool m_oldBatcherEnable;

        /// <summary>
        /// 性能记录器条目类
        /// 用于存储单个性能采样器的数据
        /// </summary>
        internal class RecorderEntry
        {
            public string name;              // 采样器名称
            public string oldName;           // 旧版本采样器名称（兼容性）
            public int callCount;            // 调用次数累计
            public float accTime;            // 累计时间（毫秒）
            public UnityEngine.Profiling.Recorder recorder;  // Unity 性能记录器引用
        };

        /// <summary>
        /// SRP Batcher 性能标记枚举
        /// 定义需要监控的各种渲染操作类型
        /// </summary>
        enum SRPBMarkers
        {
            kStdRenderDraw,              // 标准渲染绘制
            kStdShadowDraw,              // 标准阴影绘制
            kSRPBRenderDraw,             // SRP Batcher 渲染绘制
            kSRPBShadowDraw,             // SRP Batcher 阴影绘制
            kRenderThreadIdle,           // 渲染线程空闲时间
            kStdRenderApplyShader,       // 标准渲染应用着色器
            kStdShadowApplyShader,       // 标准阴影应用着色器
            kSRPBRenderApplyShader,      // SRP Batcher 渲染应用着色器
            kSRPBShadowApplyShader,      // SRP Batcher 阴影应用着色器
            BRG_DebrisGPUSetData,        // BRG 碎片 GPU 数据设置
            BRG_BackgroundGPUSetData,    // BRG 背景 GPU 数据设置
        };

        /// <summary>
        /// 性能记录器列表
        /// 警告：列表顺序必须与 SRPBMarkers 枚举完全一致
        /// </summary>
        RecorderEntry[] recordersList =
        {
            new RecorderEntry() { name="RenderLoop.Draw" },           // 渲染循环绘制
            new RecorderEntry() { name="Shadows.Draw" },              // 阴影绘制
            new RecorderEntry() { name="RenderLoop.DrawSRPBatcher" }, // SRP Batcher 渲染循环绘制
            new RecorderEntry() { name="Shadows.DrawSRPBatcher" },    // SRP Batcher 阴影绘制
            new RecorderEntry() { name="RenderLoopDevice.Idle" },     // 渲染设备空闲
            new RecorderEntry() { name="StdRender.ApplyShader" },     // 标准渲染应用着色器
            new RecorderEntry() { name="StdShadow.ApplyShader" },     // 标准阴影应用着色器
            new RecorderEntry() { name="SRPBRender.ApplyShader" },    // SRP Batcher 渲染应用着色器
            new RecorderEntry() { name="SRPBShadow.ApplyShader" },    // SRP Batcher 阴影应用着色器
            new RecorderEntry() { name="BRG_Debris.GPUSetData" },     // BRG 碎片 GPU 数据设置
            new RecorderEntry() { name="BRG_Background.GPUSetData" }, // BRG 背景 GPU 数据设置
        };

        /// <summary>
        /// 初始化方法
        /// 在游戏对象唤醒时调用，初始化所有性能记录器
        /// </summary>
        void Awake()
        {
            // 遍历所有记录器条目，获取对应的 Unity Sampler
            for (int i = 0; i < recordersList.Length; i++)
            {
                var sampler = Sampler.Get(recordersList[i].name);
                if (sampler.isValid)
                    recordersList[i].recorder = sampler.GetRecorder();
                // 如果主名称无效，尝试使用旧名称（向后兼容）
                else if ( recordersList[i].oldName != null )
                {
                    sampler = Sampler.Get(recordersList[i].oldName);
                    if (sampler.isValid)
                        recordersList[i].recorder = sampler.GetRecorder();
                }
            }

            // 初始化 GUI 样式
            m_style = new GUIStyle();
            m_style.fontSize = 15;
            m_style.normal.textColor = Color.white;

            // 记录初始的 SRP Batcher 状态
            m_oldBatcherEnable = m_Enable;

            // 重置统计数据
            ResetStats();
        }

        /// <summary>
        /// 清零所有计数器
        /// 重置累计时间、帧数和所有记录器的数据
        /// </summary>
        void RazCounters()
        {
            m_AccDeltaTime = 0.0f;
            m_frameCount = 0;
            for (int i = 0; i < recordersList.Length; i++)
            {
                recordersList[i].accTime = 0.0f;
                recordersList[i].callCount = 0;
            }
        }

        /// <summary>
        /// 重置统计信息
        /// 显示"正在收集数据..."并清零计数器
        /// </summary>
        void ResetStats()
        {
            m_statsLabel = "Gathering data...";  // 正在收集数据...
            RazCounters();
        }

        /// <summary>
        /// 切换统计显示状态
        /// </summary>
        void ToggleStats()
        {
            m_Enable = !m_Enable;
            ResetStats();
        }

        /// <summary>
        /// 每帧更新方法
        /// 处理输入检测和性能数据收集
        /// </summary>
        void Update()
        {
            // F9 键：切换 SRP Batcher 开关
            if (Input.GetKeyDown(KeyCode.F9))
            {
                GraphicsSettings.useScriptableRenderPipelineBatching = !GraphicsSettings.useScriptableRenderPipelineBatching;
            }

            // 检测 SRP Batcher 状态是否发生变化，如果变化则重置统计
            if (GraphicsSettings.useScriptableRenderPipelineBatching != m_oldBatcherEnable )
            {
                ResetStats();
                m_oldBatcherEnable = GraphicsSettings.useScriptableRenderPipelineBatching;
            }

            // F8 键或双指触摸：切换统计显示
            bool toggleStats = Input.GetKeyDown(KeyCode.F8);
            if (Input.touchCount == 2)
            {
                // 检测双指同时按下
                if ((Input.touches[0].phase == TouchPhase.Began) &&
                    (Input.touches[1].phase == TouchPhase.Began))
                    toggleStats = !toggleStats;
            }

            if (toggleStats)
            {
                ToggleStats();
            }

            // 如果启用了统计显示，收集性能数据
            if (m_Enable)
            {
                bool SRPBatcher = GraphicsSettings.useScriptableRenderPipelineBatching;

                // 累计时间和帧数
                m_AccDeltaTime += Time.unscaledDeltaTime;
                m_frameCount++;

                // 获取每个记录器的时间数据并累加
                for (int i = 0; i < recordersList.Length; i++)
                {
                    if ( recordersList[i].recorder != null )
                    {
                        // 将纳秒转换为毫秒并累加
                        recordersList[i].accTime += recordersList[i].recorder.elapsedNanoseconds / 1000000.0f;
                        recordersList[i].callCount += recordersList[i].recorder.sampleBlockCount;
                    }
                }

                // 达到统计间隔时间后，计算并显示平均值
                if (m_AccDeltaTime >= kAverageStatDuration)
                {
                    // 计算每帧平均值的系数
                    float ooFrameCount = 1.0f / (float)m_frameCount;

                    // 计算各项渲染操作的平均耗时
                    float avgStdRender = recordersList[(int)SRPBMarkers.kStdRenderDraw].accTime * ooFrameCount;
                    float avgStdShadow = recordersList[(int)SRPBMarkers.kStdShadowDraw].accTime * ooFrameCount;
                    float avgSRPBRender = recordersList[(int)SRPBMarkers.kSRPBRenderDraw].accTime * ooFrameCount;
                    float avgSRPBShadow = recordersList[(int)SRPBMarkers.kSRPBShadowDraw].accTime * ooFrameCount;
                    float RTIdleTime = recordersList[(int)SRPBMarkers.kRenderThreadIdle].accTime * ooFrameCount;

                    // 构建统计信息字符串
                    // 显示 RenderLoop.Draw 和 ShadowLoop.Draw 的累计时间（所有线程）
                    m_statsLabel = string.Format("Accumulated time for RenderLoop.Draw and ShadowLoop.Draw (all threads)\n{0:F2}ms CPU Rendering time ( incl {1:F2}ms RT idle )\n", avgStdRender + avgStdShadow + avgSRPBRender + avgSRPBShadow, RTIdleTime);

                    // 如果启用了 SRP Batcher，显示详细的 SRP Batcher 统计
                    if (SRPBatcher)
                    {
                        m_statsLabel += string.Format("  {0:F2}ms SRP Batcher code path\n", avgSRPBRender + avgSRPBShadow);
                        m_statsLabel += string.Format("    {0:F2}ms All objects ( {1} ApplyShader calls )\n", avgSRPBRender, recordersList[(int)SRPBMarkers.kSRPBRenderApplyShader].callCount / m_frameCount);
                        m_statsLabel += string.Format("    {0:F2}ms Shadows ( {1} ApplyShader calls )\n", avgSRPBShadow, recordersList[(int)SRPBMarkers.kSRPBShadowApplyShader].callCount / m_frameCount);
                    }

                    // 以下为标准渲染路径的统计（已注释）
//                     m_statsLabel += string.Format("  {0:F2}ms Standard code path\n", avgStdRender + avgStdShadow);
//                     m_statsLabel += string.Format("    {0:F2}ms All objects ( {1} ApplyShader calls )\n", avgStdRender, recordersList[(int)SRPBMarkers.kStdRenderApplyShader].callCount / m_frameCount);
//                     m_statsLabel += string.Format("    {0:F2}ms Shadows ( {1} ApplyShader calls )\n", avgStdShadow, recordersList[(int)SRPBMarkers.kStdShadowApplyShader].callCount / m_frameCount);

                    // 显示全局主循环时间和 FPS
                    m_statsLabel += string.Format("Global Main Loop: {0:F2}ms ({1} FPS)\n", m_AccDeltaTime * 1000.0f * ooFrameCount, (int)(((float)m_frameCount) / m_AccDeltaTime));

                    // 显示 BRG（BatchRendererGroup）相关统计
                    m_statsLabel += string.Format("\n");
                    m_statsLabel += string.Format("    {0:F2}ms BRG_DebrisGPUSetData ( {1} calls )\n", recordersList[(int)SRPBMarkers.BRG_DebrisGPUSetData].accTime * ooFrameCount, recordersList[(int)SRPBMarkers.BRG_DebrisGPUSetData].callCount / m_frameCount);
                    m_statsLabel += string.Format("    {0:F2}ms BRG_BackgroundGPUSetData ( {1} calls )\n", recordersList[(int)SRPBMarkers.BRG_BackgroundGPUSetData].accTime * ooFrameCount, recordersList[(int)SRPBMarkers.BRG_BackgroundGPUSetData].callCount / m_frameCount);

                    // 重置计数器，开始下一轮统计
                    RazCounters();
                }
            }
        }

        /// <summary>
        /// GUI 绘制方法
        /// 在屏幕上显示性能统计面板
        /// </summary>
        void OnGUI()
        {
            float offset = 50;
            if (m_Enable)
            {
                bool SRPBatcher = GraphicsSettings.useScriptableRenderPipelineBatching;

                // 设置 GUI 颜色为白色
                GUI.color = new Color(1, 1, 1, 1);

                // 定义面板尺寸
                float w = 700, h = 256;
                offset += h + 50;

                // 根据 SRP Batcher 状态显示不同的窗口标题
                if ( SRPBatcher )
                    GUILayout.BeginArea(new Rect(32, 50, w, h), "SRP batcher ON (F9)", GUI.skin.window);
                else
                    GUILayout.BeginArea(new Rect(32, 50, w, h), "SRP batcher OFF (F9)", GUI.skin.window);

                // 显示统计信息文本
                GUILayout.Label(m_statsLabel, m_style);

                GUILayout.EndArea();
            }
        }
    }
}
