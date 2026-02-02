using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 敌人移动类 - 控制敌人的行为和爆炸效果
///
/// 功能说明：
/// 1. 存储敌人的随机色相值（用于爆炸碎片颜色）
/// 2. 提供爆炸方法，生成大量碎片粒子
///
/// 注意：
/// 敌人的实际移动由WaveSpawner控制，此类主要负责爆炸效果
///
/// 使用方法：
/// 将此脚本挂载到敌人预制体上
/// </summary>
public class EnemyMovement : MonoBehaviour
{
    /// <summary>
    /// 敌人的随机色相值（0-1范围）
    /// 由WaveSpawner在生成时设置，用于爆炸时碎片的颜色
    /// </summary>
    public float m_rndHueColor;

    /// <summary>
    /// 初始化方法 - 目前为空，预留扩展
    /// </summary>
    private void Start()
    {
    }

    /// <summary>
    /// 每帧更新方法 - 目前为空
    /// 敌人的移动由WaveSpawner统一控制
    /// </summary>
    void Update()
    {
    }

    /// <summary>
    /// 爆炸方法 - 当敌人被击中时调用
    /// 在敌人位置生成大量碎片粒子，颜色与敌人颜色相近
    /// </summary>
    public void Explode()
    {
        // 检查碎片管理器是否存在
        if (BRG_Debris.gDebrisManager != null)
        {
            // 在当前位置生成1024个碎片粒子
            // 参数：位置、碎片数量、色相值
            BRG_Debris.gDebrisManager.GenerateBurstOfDebris(transform.position, 1024, m_rndHueColor);
        }
    }

    /// <summary>
    /// 销毁时调用 - 目前为空，预留扩展
    /// 可用于清理资源或触发其他效果
    /// </summary>
    void OnDestroy()
    {
    }
}
