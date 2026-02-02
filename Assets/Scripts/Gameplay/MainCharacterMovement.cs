using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;

/// <summary>
/// 主角移动类 - 控制玩家角色的移动、射击和受伤效果
///
/// 功能说明：
/// 1. 主角自动进行正弦波形左右移动
/// 2. 支持自动射击和手动射击（空格键/触屏）
/// 3. 被敌人撞击时触发无敌状态和屏幕震动效果
/// 4. 根据移动方向自动调整角色倾斜角度
///
/// 使用方法：
/// 将此脚本挂载到主角对象上，设置子弹预制体和射击点
/// </summary>
public class MainCharacterMovement: MonoBehaviour
{
    // ==================== 公开配置参数 ====================

    /// <summary>
    /// 自动射击间隔时间（秒），设为0或负数禁用自动射击
    /// </summary>
    public float m_autoShootPeriod = 1.5f;

    /// <summary>
    /// 正弦波移动的振幅（左右移动的最大距离）
    /// </summary>
    public float m_sinAmplitude = 8.0f;

    /// <summary>
    /// 正弦波移动的速度（角速度）
    /// </summary>
    public float m_sinSpeed = 1.0f;

    /// <summary>
    /// 子弹预制体对象
    /// </summary>
    public GameObject bulletPrefab;

    /// <summary>
    /// 射击点对象（子弹生成位置）
    /// </summary>
    public GameObject shootingPoint;

    // ==================== 私有运行时变量 ====================

    /// <summary>
    /// 自动射击计时器
    /// </summary>
    private float m_autoShootTimer;

    /// <summary>
    /// 主角初始位置
    /// </summary>
    private Vector3 m_initPos;

    /// <summary>
    /// 正弦波移动的当前相位
    /// </summary>
    private float m_sinPhase;

    /// <summary>
    /// 主角材质引用（用于改变颜色）
    /// </summary>
    private Material m_material;

    /// <summary>
    /// 主角原始颜色
    /// </summary>
    private Color m_originalColor;

    /// <summary>
    /// 主摄像机引用
    /// </summary>
    private Camera m_mainCamera;

    /// <summary>
    /// 摄像机原始位置
    /// </summary>
    private Vector3 m_originalCameraPos;

    /// <summary>
    /// 无敌状态持续时间常量（秒）
    /// </summary>
    private const float kInvincibleDuration = 3.0f;

    /// <summary>
    /// 无敌状态剩余时间计时器
    /// </summary>
    private float m_invicibleTimer;

    /// <summary>
    /// 屏幕震动持续时间常量（秒）
    /// </summary>
    private const float kShakeDuration = 1.0f;

    /// <summary>
    /// 屏幕震动的相位（用于生成震动波形）
    /// </summary>
    private float m_shakePhase;

    /// <summary>
    /// 屏幕震动剩余时间计时器
    /// </summary>
    private float m_shakeTimer;

    /// <summary>
    /// 唤醒时调用 - 记录初始位置
    /// </summary>
    private void Awake()
    {
        m_initPos = transform.position;
    }

    /// <summary>
    /// 初始化方法 - 获取材质和摄像机引用
    /// </summary>
    void Start()
    {
        // 获取子物体的渲染器组件
        Renderer[] rdr = GetComponentsInChildren<Renderer>();
        if (rdr[0] != null)
        {
            // 获取材质引用
            m_material = rdr[0].material;
            if (m_material != null)
                // 保存原始颜色
                m_originalColor = m_material.color;
        }
        // 获取主摄像机引用
        m_mainCamera = Camera.main;
        // 保存摄像机原始位置
        m_originalCameraPos = m_mainCamera.transform.position;
        // 初始化无敌计时器
        m_invicibleTimer = 0.0f;
    }


    /// <summary>
    /// 每帧更新方法
    /// 处理移动、旋转、无敌状态、屏幕震动和射击
    /// </summary>
    void Update()
    {
        float dt = Time.deltaTime;

        // ==================== 正弦波移动 ====================
        Vector3 newPos = m_initPos;
        // 使用正弦函数计算X轴偏移
        newPos.x += m_sinAmplitude * math.sin(m_sinPhase);
        // 更新相位
        m_sinPhase += dt * m_sinSpeed;
        // 应用新位置
        transform.position = newPos;

        // ==================== 根据移动方向倾斜 ====================
        // 计算正弦函数的导数（余弦），用于确定移动方向
        float derivative = math.cos(m_sinPhase);
        // 根据移动方向设置Z轴旋转角度（最大±25度）
        transform.rotation = Quaternion.AngleAxis(-derivative * 25.0f, Vector3.forward);

        // ==================== 无敌状态处理 ====================
        if (m_invicibleTimer > 0.0f)
        {
            // 减少无敌时间
            m_invicibleTimer -= dt;
            if (m_invicibleTimer < 0.0f)
                m_invicibleTimer = 0.0f;

            // 根据剩余无敌时间，在原始颜色和红色之间插值
            Color updatedColor = Color.Lerp(m_originalColor, Color.red, m_invicibleTimer / kInvincibleDuration);
            if (m_material != null)
            {
                // 应用颜色变化
                m_material.color = updatedColor;
            }
        }

        // ==================== 屏幕震动处理 ====================
        if (m_shakeTimer > 0.0f)
        {
            // 减少震动时间
            m_shakeTimer -= dt;
            if (m_shakeTimer < 0.0f)
                m_shakeTimer = 0.0f;

            // 更新震动相位
            m_shakePhase += dt * 10.0f;
            // 根据剩余时间计算震动幅度（逐渐减弱）
            float shakeAmplitude = (m_shakeTimer / kShakeDuration) * 0.5f;
            // 使用不同频率的正弦波叠加产生随机感的震动效果
            m_mainCamera.transform.position = m_originalCameraPos + new Vector3(
                shakeAmplitude * math.sin(m_shakePhase),           // X轴震动
                shakeAmplitude * math.sin(m_shakePhase * 1.37f),   // Y轴震动（不同频率）
                shakeAmplitude * math.sin(m_shakePhase * 2.17f)    // Z轴震动（不同频率）
            );
        }

        // ==================== 射击输入检测 ====================
        /*
        // 预留的手动移动控制代码
        float xMov = Input.GetAxisRaw("Horizontal");
        float yMov = Input.GetAxisRaw("Vertical");
        */

        // 检测空格键射击
        bool fire = Input.GetKeyDown(KeyCode.Space);

        // 检测触屏射击（单指触摸）
        if (Input.touchCount == 1)
        {
            if (Input.touches[0].phase == TouchPhase.Began)
                fire = true;
        }

        // ==================== 自动射击处理 ====================
        if (m_autoShootPeriod > 0.0f)
        {
            m_autoShootTimer += dt;
            // 检查是否到达自动射击时间
            if (m_autoShootTimer >= m_autoShootPeriod)
            {
                fire = true;
                m_autoShootTimer = 0.0f;
            }
        }

        // ==================== 执行射击 ====================
        if (fire)
        {
            // 在射击点位置实例化子弹
            var bulletInstance = Instantiate(bulletPrefab, shootingPoint.transform.position, shootingPoint.transform.rotation);
        }
    }

    /// <summary>
    /// 触发器碰撞检测 - 当敌人撞击玩家时调用
    /// 触发敌人爆炸、屏幕震动和无敌状态
    /// </summary>
    /// <param name="other">碰撞到的其他碰撞体</param>
    private void OnTriggerEnter(Collider other)
    {
        // 检查碰撞对象是否为敌人
        if (other.gameObject.tag == "Enemy")
        {
            // 获取敌人组件
            EnemyMovement enemy = other.gameObject.GetComponent<EnemyMovement>();
            if (enemy != null)
            {
                // 如果当前没有震动，开始新的震动
                if (m_shakeTimer <= 0.0f)
                    m_shakeTimer = kShakeDuration;

                // 进入无敌状态
                m_invicibleTimer = kInvincibleDuration;

                // 触发敌人爆炸效果
                enemy.Explode();
            }
            // 销毁敌人
            Destroy(other.gameObject);
        }
    }
}
