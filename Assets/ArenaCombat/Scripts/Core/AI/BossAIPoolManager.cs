using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using ArenaCombat.Core.Network;

namespace ArenaCombat.Core.AI
{
    [DisallowMultipleComponent]
    public class BossAIPoolManager : MonoBehaviour
    {
        public static BossAIPoolManager Instance { get; private set; }

        [Header("Pair Pool (10 combos)")]
        [SerializeField] private BossAIDefinition _defaultAI;
        [SerializeField] private BossAIDefinition[] _combos = new BossAIDefinition[10];

        [Header("Debug")]
        [SerializeField] private bool _verboseLog = true;
        [SerializeField] private string _debugCurrentVariant;
        [SerializeField] private string _debugPairKey;
        [SerializeField] private string _debugPending;

        private Dictionary<(PlayerArchetype, PlayerArchetype), BossAIDefinition> _pairPool;
        private BossAIDefinition _currentDef;
        private BossAIDefinition _pendingDef;
        private BossNetworkController3D _bossController;
        private GameStateManager _subscribedGSM;
        private PlayerArchetypeClassifier _subscribedClassifier;
        private bool _needsEvaluate;

        public event System.Action OnVariantSlotsApplied;

        public BossAIDefinition CurrentVariant => _currentDef;
        public BossAIDefinition PendingVariant => _pendingDef;

        void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(this); return; }
            BuildPairPool();
        }

        void OnValidate()
        {
            if (_combos == null || _combos.Length != 10)
                System.Array.Resize(ref _combos, 10);
        }

        void OnEnable()
        {
            TrySubscribe();
        }

        void OnDisable()
        {
            UnsubscribeAll();
        }

        void OnDestroy()
        {
            UnsubscribeAll();
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (_subscribedGSM == null || _subscribedClassifier == null)
                TrySubscribe();

            if (_needsEvaluate && IsServer)
                EvaluateAndSwap();
        }

        void BuildPairPool()
        {
            _pairPool = new Dictionary<(PlayerArchetype, PlayerArchetype), BossAIDefinition>();
            if (_combos == null) return;

            foreach (var def in _combos)
            {
                if (def == null) continue;
                if (def.isDefault)
                {
                    Debug.LogWarning($"[BossAIPool] {def.name} is flagged isDefault — assign to _defaultAI, not _combos[].", this);
                    continue;
                }
                var key = def.PairKey;
                if (_pairPool.ContainsKey(key))
                {
                    Debug.LogWarning($"[BossAIPool] Duplicate pair ({key.Item1},{key.Item2}) — keeping first, ignoring {def.name}.", this);
                    continue;
                }
                _pairPool[key] = def;
            }

            if (_defaultAI == null)
                Debug.LogWarning("[BossAIPool] _defaultAI is null — cold-start fallback will be null.", this);
            if (_pairPool.Count < 10)
                Debug.LogWarning($"[BossAIPool] Only {_pairPool.Count}/10 pairs wired.", this);
        }

        static bool IsServer => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

        void TrySubscribe()
        {
            if (_subscribedGSM == null && GameStateManager.Instance != null)
            {
                _subscribedGSM = GameStateManager.Instance;
                _subscribedGSM.OnMatchStateChanged += HandleMatchStateChanged;
                if (IsServer && _subscribedGSM.CurrentMatchState == MatchState.InProgress)
                    _needsEvaluate = true;
            }
            if (_subscribedClassifier == null && PlayerArchetypeClassifier.Instance != null)
            {
                _subscribedClassifier = PlayerArchetypeClassifier.Instance;
                _subscribedClassifier.OnPlayerArchetypeChanged += HandlePlayerArchetypeChanged;
            }
        }

        void UnsubscribeAll()
        {
            if (_subscribedGSM != null)
            {
                _subscribedGSM.OnMatchStateChanged -= HandleMatchStateChanged;
                _subscribedGSM = null;
            }
            if (_subscribedClassifier != null)
            {
                _subscribedClassifier.OnPlayerArchetypeChanged -= HandlePlayerArchetypeChanged;
                _subscribedClassifier = null;
            }
            UnsubscribeBoss();
        }

        void UnsubscribeBoss()
        {
            if (_bossController != null)
            {
                _bossController.OnIdleAfterAction -= ApplyPending;
                _bossController.OnPhaseSkillsPopulated -= HandlePhaseSkillsPopulated;
                _bossController = null;
            }
        }

        void HandleMatchStateChanged(MatchState prev, MatchState next)
        {
            if (!IsServer) return;
            if (next == MatchState.WaitingForPlayers)
            {
                ResetState();
                return;
            }
            if (next == MatchState.InProgress)
            {
                _needsEvaluate = true;
                EvaluateAndSwap();
            }
        }

        void HandlePlayerArchetypeChanged(ulong clientId, PlayerArchetype oldType, PlayerArchetype newType)
        {
            if (!IsServer) return;
            EvaluateAndSwap();
        }

        void ResetState()
        {
            UnsubscribeBoss();
            _currentDef = null;
            _pendingDef = null;
            _needsEvaluate = false;
            _debugCurrentVariant = "";
            _debugPairKey = "";
            _debugPending = "";
        }

        BossNetworkController3D ResolveBossController()
        {
            if (BossManager.Instance == null) return null;
            var nob = BossManager.Instance.CurrentBoss;
            if (nob == null)
            {
                if (_bossController != null) UnsubscribeBoss();
                _bossController = null;
                return null;
            }
            if (_bossController == null || _bossController.NetworkObject != nob)
            {
                if (_bossController != null) UnsubscribeBoss();
                _bossController = nob.GetComponent<BossNetworkController3D>();
                if (_bossController != null)
                    _bossController.OnPhaseSkillsPopulated += HandlePhaseSkillsPopulated;
            }
            return _bossController;
        }

        bool ResolvePairKey(out (PlayerArchetype, PlayerArchetype) pairKey)
        {
            pairKey = (PlayerArchetype.Hybrid, PlayerArchetype.Hybrid);

            var classifier = PlayerArchetypeClassifier.Instance;
            var nm = NetworkManager.Singleton;
            if (classifier == null || nm == null)
                return false;

            PlayerArchetype a1 = PlayerArchetype.Hybrid;
            PlayerArchetype a2 = PlayerArchetype.Hybrid;
            int playerCount = 0;

            foreach (var client in nm.ConnectedClientsList)
            {
                if (client.PlayerObject == null) continue;
                var arch = classifier.GetArchetype(client.ClientId);
                if (playerCount == 0) a1 = arch;
                else if (playerCount == 1) a2 = arch;
                playerCount++;
                if (playerCount >= 2) break;
            }

            if (playerCount == 0)
                return false;

            if (playerCount == 1)
                a2 = a1;

            pairKey = a1 <= a2 ? (a1, a2) : (a2, a1);
            return true;
        }

        public void EvaluateAndSwap()
        {
            if (!IsServer) return;
            var gsm = GameStateManager.Instance;
            if (gsm == null || gsm.CurrentMatchState != MatchState.InProgress)
            {
                _needsEvaluate = false;
                return;
            }

            var boss = ResolveBossController();
            if (boss == null)
            {
                _needsEvaluate = true;
                return;
            }

            BossAIDefinition def = null;

            if (ResolvePairKey(out var pairKey))
            {
                if (_pairPool.TryGetValue(pairKey, out var found))
                    def = found;
            }
            def ??= _defaultAI;

            if (def == null)
            {
                _needsEvaluate = false;
                return;
            }

            if (def == _currentDef)
            {
                if (_pendingDef != null && boss != null)
                {
                    boss.OnIdleAfterAction -= ApplyPending;
                    _pendingDef = null;
                    if (_verboseLog) Debug.Log("[BossAI] pending swap cancelled (resolved back to current).", this);
                }
                _needsEvaluate = false;
                return;
            }

            if (boss.IsBusy)
            {
                _pendingDef = def;
                boss.OnIdleAfterAction -= ApplyPending;
                boss.OnIdleAfterAction += ApplyPending;
                _needsEvaluate = false;
                _debugPending = def.name;
                if (_verboseLog) Debug.Log($"[BossAI] swap deferred → {def.name} (boss busy)", this);
                return;
            }

            ApplyVariant(boss, def, pairKey);
        }

        void ApplyVariant(BossNetworkController3D boss, BossAIDefinition def, (PlayerArchetype, PlayerArchetype) pairKey)
        {
            boss.ApplyAIVariant(def);

            if (def.model != null)
            {
                var agent = boss.GetComponent<BossInferenceAgent>();
                if (agent != null)
                    agent.SetModel("BossInference", def.model);
            }

            _currentDef = def;
            _pendingDef = null;
            _needsEvaluate = false;
            _debugCurrentVariant = def.name;
            _debugPairKey = $"{pairKey.Item1}+{pairKey.Item2}";
            _debugPending = "";
            if (_verboseLog) Debug.Log($"[BossAI] swap applied: {def.name} model={def.model?.name ?? "none"} (pair={pairKey.Item1}+{pairKey.Item2})", this);
            OnVariantSlotsApplied?.Invoke();
        }

        void ApplyPending()
        {
            if (_bossController != null)
                _bossController.OnIdleAfterAction -= ApplyPending;

            if (!IsServer) { _pendingDef = null; return; }

            _pendingDef = null;
            EvaluateAndSwap();
        }

        void HandlePhaseSkillsPopulated()
        {
            if (!IsServer || _currentDef == null || _bossController == null) return;
            _bossController.ApplyAIVariantSlots(_currentDef);
            OnVariantSlotsApplied?.Invoke();
        }
    }
}
