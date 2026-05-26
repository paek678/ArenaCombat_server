using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents.Sensors;
using ArenaCombat.Core.Network;
using ArenaCombat.Core.Skill;
using ArenaCombat.Core.Stats;

namespace ArenaCombat.Core.AI
{
    public class BossObservationCollector : MonoBehaviour
    {
        public const int TotalObsSize = 129;
        public const int BossSkillSlots = 5;
        public const int PlayerSkillSlots = 5;
        public const int ChannelsPerSlot = 7;

        [Header("Boss References")]
        [SerializeField] private BossNetworkController3D _bnc;
        [SerializeField] private StatManager _bossStatManager;
        [SerializeField] private SkillExecutor _skillExecutor;
        [SerializeField] private SkillManager _skillManager;

        [Header("Observation Limits")]
        [SerializeField] private float _maxDistance = 55f;
        [SerializeField] private float _maxCooldown = 30f;
        [SerializeField] private float _maxBurstDmg = 80f;
        [SerializeField] private float _maxSpeed = 16f;
        [SerializeField] private int _maxBossPhase = 4;

        private const float TouchRange = 5f;
        private const float ConeNorm = 180f;
        private const float AoeNorm = 25f;

        private GameObject _p1;
        private GameObject _p2;
        private StatManager _p1StatManager;
        private StatManager _p2StatManager;
        private SkillManager _p1SkillManager;
        private SkillManager _p2SkillManager;
        private SkillExecutor _p1SkillExecutor;
        private SkillExecutor _p2SkillExecutor;

        private float _prevBossHp;
        private readonly Queue<(float time, float damage)> _damageLog = new();
        private float _recentDamageSum;

        private Vector3 _prevP1Pos;
        private Vector3 _prevP2Pos;
        private float _p1AvgSpeed;
        private float _p2AvgSpeed;
        private bool _initialized;

        private const float SpeedSmooth = 0.15f;

        public float P1AvgSpeed => _p1AvgSpeed;
        public float P2AvgSpeed => _p2AvgSpeed;
        public float RecentBurstDamage => _recentDamageSum;
        public GameObject P1 => _p1;
        public GameObject P2 => _p2;

        private void Awake()
        {
            if (_bnc == null) _bnc = GetComponent<BossNetworkController3D>();
            if (_bossStatManager == null) _bossStatManager = GetComponent<StatManager>();
            if (_skillExecutor == null) _skillExecutor = GetComponent<SkillExecutor>();
            if (_skillManager == null) _skillManager = GetComponent<SkillManager>();
        }

        private void Start()
        {
            RefreshPlayerCache();
        }

        public void RefreshPlayerCache()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            _p1 = gm.Player1;
            _p2 = gm.Player2;

            if (_p1 != null)
            {
                _p1StatManager = _p1.GetComponent<StatManager>();
                _p1SkillManager = _p1.GetComponent<SkillManager>();
                _p1SkillExecutor = _p1.GetComponent<SkillExecutor>();
                _prevP1Pos = _p1.transform.position;
            }
            if (_p2 != null)
            {
                _p2StatManager = _p2.GetComponent<StatManager>();
                _p2SkillManager = _p2.GetComponent<SkillManager>();
                _p2SkillExecutor = _p2.GetComponent<SkillExecutor>();
                _prevP2Pos = _p2.transform.position;
            }

            if (_bossStatManager != null)
                _prevBossHp = _bossStatManager.GetHP();

            _initialized = true;
        }

        private void Update()
        {
            if (!_initialized) return;
            TrackBurstDamage();
            TrackMovementSpeed();
        }

        private void TrackBurstDamage()
        {
            if (_bossStatManager == null) return;

            float currentHp = _bossStatManager.GetHP();
            float delta = _prevBossHp - currentHp;
            if (delta > 0f)
            {
                _damageLog.Enqueue((Time.time, delta));
                _recentDamageSum += delta;
            }
            _prevBossHp = currentHp;

            while (_damageLog.Count > 0 && Time.time - _damageLog.Peek().time > 1f)
                _recentDamageSum -= _damageLog.Dequeue().damage;

            if (_recentDamageSum < 0f) _recentDamageSum = 0f;
        }

        private void TrackMovementSpeed()
        {
            float dt = Mathf.Max(Time.deltaTime, 0.001f);
            if (_p1 != null)
            {
                float s = Vector3.Distance(_p1.transform.position, _prevP1Pos) / dt;
                _p1AvgSpeed = Mathf.Lerp(_p1AvgSpeed, s, SpeedSmooth);
                _prevP1Pos = _p1.transform.position;
            }
            if (_p2 != null)
            {
                float s = Vector3.Distance(_p2.transform.position, _prevP2Pos) / dt;
                _p2AvgSpeed = Mathf.Lerp(_p2AvgSpeed, s, SpeedSmooth);
                _prevP2Pos = _p2.transform.position;
            }
        }

        // ═══════════════════════════════════════════════════
        // 129-channel observation matching training ONNX spec
        // ═══════════════════════════════════════════════════

        public void CollectFull129(VectorSensor sensor)
        {
            CollectPositions(sensor);
            CollectState(sensor);
            CollectEntitySlots(sensor, _skillManager, _skillExecutor, BossSkillSlots);
            CollectTouch(sensor);
            CollectEntitySlots(sensor, _p1SkillManager, _p1SkillExecutor, PlayerSkillSlots);
            CollectEntitySlots(sensor, _p2SkillManager, _p2SkillExecutor, PlayerSkillSlots);
            CollectExtra(sensor);
        }

        // #0-10: Position / Direction
        private void CollectPositions(VectorSensor sensor)
        {
            Vector3 bossPos = transform.position;
            Vector3 fwd = transform.forward;

            if (_p1 != null)
            {
                Vector3 toP1 = _p1.transform.position - bossPos;
                float distP1 = toP1.magnitude;
                Vector3 dirP1 = distP1 > 0.001f ? toP1.normalized : Vector3.forward;
                sensor.AddObservation(dirP1.x);
                sensor.AddObservation(dirP1.z);
                sensor.AddObservation(Mathf.Clamp01(distP1 / _maxDistance));
                sensor.AddObservation(fwd.x);
                sensor.AddObservation(fwd.z);
                sensor.AddObservation(Vector3.Dot(fwd, dirP1));
            }
            else
            {
                sensor.AddObservation(new float[6]);
            }

            if (_p2 != null)
            {
                Vector3 toP2 = _p2.transform.position - bossPos;
                float distP2 = toP2.magnitude;
                Vector3 dirP2 = distP2 > 0.001f ? toP2.normalized : Vector3.forward;
                sensor.AddObservation(dirP2.x);
                sensor.AddObservation(dirP2.z);
                sensor.AddObservation(Mathf.Clamp01(distP2 / _maxDistance));

                float distPP = _p1 != null
                    ? Vector3.Distance(_p1.transform.position, _p2.transform.position)
                    : 0f;
                sensor.AddObservation(Mathf.Clamp01(distPP / _maxDistance));
                sensor.AddObservation(Vector3.Dot(fwd, dirP2));
            }
            else
            {
                sensor.AddObservation(new float[5]);
            }
        }

        // #11-14: HP + Phase
        private void CollectState(VectorSensor sensor)
        {
            float bossHp = _bossStatManager != null ? _bossStatManager.GetHPPercent() : 0f;
            float p1Hp = _p1StatManager != null ? _p1StatManager.GetHPPercent() : 0f;
            float p2Hp = _p2StatManager != null ? _p2StatManager.GetHPPercent() : 0f;

            sensor.AddObservation(bossHp);
            sensor.AddObservation(p1Hp);
            sensor.AddObservation(p2Hp);

            if (_bnc != null)
            {
                int phase = (int)_bnc.CurrentPhase;
                sensor.AddObservation(Mathf.Clamp01((phase + 1f) / _maxBossPhase));
            }
            else
            {
                sensor.AddObservation(0f);
            }
        }

        // 5 slots × 7ch = 35ch per entity
        private void CollectEntitySlots(VectorSensor sensor, SkillManager mgr,
            SkillExecutor exec, int slotCount)
        {
            var slots = mgr != null ? mgr.Slots : null;
            for (int i = 0; i < slotCount; i++)
            {
                SkillDefinition skill = (slots != null && i < slots.Count) ? slots[i] : null;
                CollectSlot7(sensor, skill, exec);
            }
        }

        private void CollectSlot7(VectorSensor sensor, SkillDefinition skill, SkillExecutor exec)
        {
            if (skill == null)
            {
                sensor.AddObservation(new float[ChannelsPerSlot]);
                return;
            }

            float remaining = exec != null ? exec.GetRemainingCooldown(skill) : 0f;
            float effectiveCd = skill.Cooldown * (exec != null ? exec.CooldownScale : 1f);
            bool isDir = skill.AIHint_Category == SkillCategoryFlag.Directional;
            bool isAoe = skill.AIHint_Category == SkillCategoryFlag.AoE;
            bool isProj = skill.AIHint_Category == SkillCategoryFlag.Projectile;

            float coneOrAoe = 0f;
            if (isDir) coneOrAoe = skill.AIHint_ConeOrAoE / ConeNorm;
            else if (isAoe) coneOrAoe = skill.AIHint_ConeOrAoE / AoeNorm;

            sensor.AddObservation(Mathf.Clamp01(remaining / _maxCooldown));
            sensor.AddObservation(Mathf.Clamp01(effectiveCd / _maxCooldown));
            sensor.AddObservation(Mathf.Clamp01(skill.Range / _maxDistance));
            sensor.AddObservation(Mathf.Clamp01(coneOrAoe));
            sensor.AddObservation(isDir ? 1f : 0f);
            sensor.AddObservation(isAoe ? 1f : 0f);
            sensor.AddObservation(isProj ? 1f : 0f);
        }

        // #50-51: Touch range
        private void CollectTouch(VectorSensor sensor)
        {
            float distP1 = _p1 != null
                ? Vector3.Distance(transform.position, _p1.transform.position)
                : _maxDistance;
            float distP2 = _p2 != null
                ? Vector3.Distance(transform.position, _p2.transform.position)
                : _maxDistance;

            sensor.AddObservation(distP1 < TouchRange ? 1f : 0f);
            sensor.AddObservation(distP2 < TouchRange ? 1f : 0f);
        }

        // #122-128: Casting, speed, unlocked ratio, burst damage
        private void CollectExtra(VectorSensor sensor)
        {
            bool p1Alive = _p1StatManager != null && _p1StatManager.IsAlive;
            bool p2Alive = _p2StatManager != null && _p2StatManager.IsAlive;

            sensor.AddObservation(p1Alive && _p1StatManager.IsCasting ? 1f : 0f);
            sensor.AddObservation(p2Alive && _p2StatManager.IsCasting ? 1f : 0f);
            sensor.AddObservation(p1Alive ? Mathf.Clamp01(_p1AvgSpeed / _maxSpeed) : 0f);
            sensor.AddObservation(p2Alive ? Mathf.Clamp01(_p2AvgSpeed / _maxSpeed) : 0f);

            int p1Unlocked = CountUnlockedSlots(_p1SkillManager);
            int p2Unlocked = CountUnlockedSlots(_p2SkillManager);
            sensor.AddObservation(p1Unlocked / (float)PlayerSkillSlots);
            sensor.AddObservation(p2Unlocked / (float)PlayerSkillSlots);

            sensor.AddObservation(Mathf.Clamp01(_recentDamageSum / _maxBurstDmg));
        }

        private int CountUnlockedSlots(SkillManager mgr)
        {
            if (mgr == null) return 0;
            var slots = mgr.Slots;
            int count = 0;
            for (int i = 0; i < slots.Count; i++)
                if (slots[i] != null) count++;
            return count;
        }

        public float GetDistanceToP1()
        {
            if (_p1 == null) return _maxDistance;
            return Vector3.Distance(transform.position, _p1.transform.position);
        }

        public float GetDistanceToP2()
        {
            if (_p2 == null) return _maxDistance;
            return Vector3.Distance(transform.position, _p2.transform.position);
        }
    }
}
