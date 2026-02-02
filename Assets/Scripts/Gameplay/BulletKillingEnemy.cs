using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 子弹击杀敌人类 - 控制子弹的移动和碰撞检测
///
/// 功能说明：
/// 1. 控制子弹向前飞行
/// 2. 检测与敌人的碰撞并触发敌人爆炸
/// 3. 子弹经过时会影响背景格子产生磁场效果
/// 4. 超出范围后自动销毁
///
/// 使用方法：
/// 将此脚本挂载到子弹预制体上，确保子弹有Collider组件并设置为Trigger
/// </summary>
public class BulletKillingEnemy : MonoBehaviour
{
    /// <summary>
    /// 子弹上一帧的位置，用于计算子弹轨迹经过的背景格子
    /// </summary>
    private Vector3 m_prevPos;

    /// <summary>
    /// 初始化方法 - 记录子弹初始位置
    /// </summary>
    private void Awake()
    {
        m_prevPos = transform.position;
    }

    /// <summary>
    /// 每帧更新方法
    /// 负责移动子弹、检测边界、触发背景磁场效果
    /// </summary>
    private void Update()
    {
        float dt = Time.deltaTime;

        // 子弹向前移动（Z轴正方向），速度为50单位/秒
        transform.Translate(0, 0, dt * 50.0f);

        // 检查子弹是否超出游戏区域（Z > 100）
        if (transform.position.z > 100)
        {
            // 销毁超出范围的子弹
            Destroy(this.gameObject);
        }
        else
        {
            // 子弹仍在游戏区域内
            // 通知背景管理器设置子弹轨迹经过的格子产生磁场效果
            if (BRG_Background.gBackgroundManager != null)
                BRG_Background.gBackgroundManager.SetMagnetCell(m_prevPos, transform.position);

            // 更新上一帧位置
            m_prevPos = transform.position;
        }
    }

    /// <summary>
    /// 触发器碰撞检测方法 - 当子弹与其他碰撞体接触时调用
    /// 检测是否击中敌人，如果是则触发敌人爆炸并销毁双方
    /// </summary>
    /// <param name="other">碰撞到的其他碰撞体</param>
    private void OnTriggerEnter(Collider other)
    {
        // 检查碰撞对象是否为敌人（通过Tag判断）
        if (other.gameObject.tag == "Enemy")
        {
            // 获取敌人的移动组件
            EnemyMovement enemy = other.gameObject.GetComponent<EnemyMovement>();
            if (enemy != null)
            {
                // 触发敌人爆炸效果（生成碎片粒子）
                enemy.Explode();
            }
            // 销毁敌人对象
            Destroy(other.gameObject);
            // 销毁子弹自身
            Destroy(this.gameObject);
        }
    }
}
