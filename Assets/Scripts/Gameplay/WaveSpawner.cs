using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 波次生成器类 - 负责按波次生成敌人
///
/// 功能说明：
/// 1. 在指定时间后开始生成敌人波次
/// 2. 敌人沿着圆弧轨迹移动（波浪形运动）
/// 3. 当所有敌人被消灭或离开游戏区域后，重置并生成新的波次
///
/// 使用方法：
/// 将此脚本挂载到空物体上，设置要生成的敌人预制体和波次参数
/// </summary>
public class WaveSpawner : MonoBehaviour
{
    // ==================== 公开配置参数 ====================

    /// <summary>
    /// 要生成的敌人预制体对象
    /// </summary>
    public GameObject m_objToSpawn;

    /// <summary>
    /// 每波敌人的数量
    /// </summary>
    public int m_waveCount;

    /// <summary>
    /// 波次开始生成的延迟时间（秒）
    /// </summary>
    public float m_startTime;

    /// <summary>
    /// 波次整体向前移动的线性速度
    /// </summary>
    public float m_waveLinearSpeed;

    /// <summary>
    /// 敌人沿圆弧运动的角速度（弧度/秒）
    /// </summary>
    public float m_waveCircularSpeed;

    /// <summary>
    /// 圆弧运动的半径
    /// </summary>
    public float m_waveRadius;

    /// <summary>
    /// 波次在圆弧上展开的角度范围（度）
    /// </summary>
    public float m_waveLenInDegree;

    // ==================== 私有运行时变量 ====================

    /// <summary>
    /// 存储已生成的敌人对象数组
    /// </summary>
    private GameObject[] m_spawnedObjects;

    /// <summary>
    /// 波次中心点的当前位置
    /// </summary>
    private Vector3 m_wavePos;

    /// <summary>
    /// 计时器，用于控制波次开始时间
    /// </summary>
    private float m_clock;

    /// <summary>
    /// 当前圆弧运动的角度（弧度）
    /// </summary>
    private float m_angle;

    /// <summary>
    /// 当前已实例化的敌人数量索引
    /// </summary>
    private int m_instantiatePos;


    /// <summary>
    /// 初始化方法 - 在游戏开始时调用
    /// 检查预制体是否设置，初始化敌人数组
    /// </summary>
    void Start()
    {
        // 检查是否设置了要生成的对象
        if (m_objToSpawn == null)
        {
            // 如果关卡设计师忘记设置生成类型，删除此生成器并输出警告
            Debug.Log("WARNING: SpawnableObject with empty m_objToSpawn field");
            Destroy(gameObject);
        }
        else
        {
            // 记录初始位置作为波次起始点
            m_wavePos = transform.position;
            // 初始化敌人对象数组
            m_spawnedObjects = new GameObject[m_waveCount];
            m_instantiatePos = 0;
        }
    }

    /// <summary>
    /// 在Scene视图中绘制Gizmo，方便编辑器中可视化生成点位置
    /// 绘制一个半透明黄色立方体标记生成器位置
    /// </summary>
    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1, 1, 0, 0.5f);  // 半透明黄色
        Gizmos.DrawCube(transform.position, new Vector3(2, 2, 2));
    }

    /// <summary>
    /// 更新波次中所有敌人的位置
    /// 敌人沿圆弧轨迹移动，同时整体向前推进
    /// </summary>
    /// <param name="dt">时间增量（deltaTime）</param>
    /// <returns>返回当前存活的敌人数量</returns>
    private int UpdateWavePos(float dt)
    {
        float angle = m_angle;  // 起始角度
        int count = 0;          // 存活敌人计数

        // 遍历所有已生成的敌人
        for (int i = 0; i < m_instantiatePos; i++)
        {
            // 检查敌人是否还存在（可能被子弹消灭）
            if (m_spawnedObjects[i] != null)
            {
                // 计算敌人在圆弧上的新位置
                Vector3 pos = transform.position;
                // 使用三角函数计算圆弧上的X和Z坐标
                pos.x = m_wavePos.x + m_waveRadius * Mathf.Cos(angle);
                pos.z = m_wavePos.z + m_waveRadius * Mathf.Sin(angle);
                m_spawnedObjects[i].transform.position = pos;

                // 检查敌人是否已经离开游戏区域（Z < 0）
                if (m_spawnedObjects[i].transform.position.z < 0)
                {
                    // 销毁离开区域的敌人
                    Destroy(m_spawnedObjects[i]);
                    m_spawnedObjects[i] = null;
                }
                else
                {
                    // 敌人仍在游戏区域内，计数+1
                    count++;
                }
            }
            // 计算下一个敌人的角度偏移
            // 将角度范围均匀分配给所有敌人
            angle += m_waveLenInDegree * 3.1415926f / (180.0f * (float)m_waveCount);
        }

        // 更新圆弧运动角度（旋转效果）
        m_angle += dt * m_waveCircularSpeed;
        // 更新波次中心点位置（向前推进）
        m_wavePos.z -= dt * m_waveLinearSpeed;

        return count;
    }


    /// <summary>
    /// 每帧更新方法
    /// 负责生成新敌人、更新敌人位置、检测波次重置
    /// </summary>
    void Update()
    {
        float dt = Time.deltaTime;
        m_clock += dt;

        // 检查是否到达开始生成时间
        if ( m_clock >= m_startTime )
        {
            // 如果还没有生成完所有敌人，继续生成
            if (m_instantiatePos < m_waveCount)
            {
                int i = m_instantiatePos;
                // 实例化新敌人
                m_spawnedObjects[i] = Instantiate(m_objToSpawn);

                // 为敌人生成随机色相值（0-1范围）
                float rndHue = UnityEngine.Random.Range(0.0f, 1.0f);

                // 获取敌人的渲染器和移动组件
                Renderer r = m_spawnedObjects[i].GetComponent<Renderer>();
                EnemyMovement enemy = m_spawnedObjects[i].GetComponent<EnemyMovement>();

                // 保存随机色相值到敌人组件（用于爆炸效果）
                enemy.m_rndHueColor = rndHue;
                // 设置敌人材质颜色（HSV转RGB，饱和度0.8，亮度1）
                r.material.color = Color.HSVToRGB(rndHue, 0.8f, 1);

                m_instantiatePos++;
            }

            // 更新所有敌人位置并获取存活数量
            int count = UpdateWavePos(dt);

            // 如果波次发射器为空（所有敌人都死亡）且已离开游戏区域，重置波次
            if ((count == 0) && (m_wavePos.z < 0.0f))
            {
                // 重置计时器
                m_clock = 0.0f;
                // 重置波次位置到初始位置
                m_wavePos = transform.position;
                // 重置实例化索引
                m_instantiatePos = 0;
            }
        }
    }
}
