using UnityEngine;

namespace ArenaCombat.Core.Combat
{

[CreateAssetMenu(fileName = "BaseStatsSO", menuName = "Scriptable Objects/BaseStatsSO")]
public class BaseStatsSO : ScriptableObject
{
    // ���� ����
    public float DamageTakenMultiplier = 1f;
    public float HealingReceivedMultiplier = 1f;

    // �̵� / �⵿ ����
    public float MoveControlMultiplier = 1f;

    // ���� �׼� ����
    public float RopeCancelResistance = 0f;

    // ���� / ��ų ����
    public float SkillCooldownMultiplier = 1f;
    public float ChannelDurationMultiplier = 1f;

    // ���� / ȸ�� ����
    public float SpawnInvulnerableDuration = 0f;

    // �����̻� / ���� ���� ����
    public float StunDurationMultiplier = 1f;
    public float CrowdControlPower = 1f;
    public float CrowdControlResistance = 0f;
    public float HitStunResistance = 0f;
    public float DebuffDurationResistance = 0f;

    // ���� / ����� ���� ����
    public float DamageUpMultiplier = 1f;
    public float DefenseUpMultiplier = 1f;
    public float VulnerabilityBonus = 0f;
    public float ReflectRatio = 0f;

}

}
