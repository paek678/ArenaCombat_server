using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using Unity.Netcode;
using ArenaCombat.Core.Network;
using ArenaCombat.Core.Skill;
using ArenaCombat.Core.Stats;
using ArenaCombat.Core.State;

// Inference-only boss ML-Agent: movement via ONNX, skills via server auto-cast.
//
// SERVER AUTHORITY: OnActionReceived gates all mutations with IsServer check.
// Movement routes through BNC3D.ApplyMLPosition (MovePosition + networkPosition NV).
//
// Observation: 129 channels (BossObservationCollector.TotalObsSize)
//   See BossObservationCollector.CollectFull129 for channel map.
//
// Actions: 1 discrete branch, size 5
//   0=idle  1=forward  2=left  3=right  4=backward

namespace ArenaCombat.Core.AI
{
    [RequireComponent(typeof(BossObservationCollector))]
    public class BossInferenceAgent : Agent
    {
        public const int TotalObsSize = BossObservationCollector.TotalObsSize;

        [Header("Movement")]
        [SerializeField] private float _moveSpeed = 8.4f;
        [SerializeField] private float _rotationSpeed = 540f;

        public void SetMoveSpeed(float speed)
        {
            _moveSpeed = Mathf.Max(0f, speed);
        }

        [Header("References")]
        [SerializeField] private BossNetworkController3D _bnc;
        [SerializeField] private BossObservationCollector _collector;
        [SerializeField] private SkillManager _skillManager;
        [SerializeField] private SkillExecutor _skillExecutor;
        [SerializeField] private StatManager _statManager;
        [SerializeField] private StateManager _stateManager;

        private bool IsServer =>
            NetworkManager.Singleton != null ? NetworkManager.Singleton.IsServer : true;

        public override void Initialize()
        {
            if (_bnc == null) _bnc = GetComponent<BossNetworkController3D>();
            if (_collector == null) _collector = GetComponent<BossObservationCollector>();
            if (_skillManager == null) _skillManager = GetComponent<SkillManager>();
            if (_skillExecutor == null) _skillExecutor = GetComponent<SkillExecutor>();
            if (_statManager == null) _statManager = GetComponent<StatManager>();
            if (_stateManager == null) _stateManager = GetComponent<StateManager>();
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            if (_collector == null)
            {
                sensor.AddObservation(new float[TotalObsSize]);
                return;
            }

            _collector.CollectFull129(sensor);
        }

        public override void OnActionReceived(ActionBuffers actionBuffers)
        {
            if (!IsServer) return;
            if (_statManager == null || !_statManager.IsAlive) return;

            int moveAction = actionBuffers.DiscreteActions[0];
            Debug.Log($"[BossML] action={moveAction} busy={(_bnc != null && _bnc.IsBusy)}");
            ApplyMovement(moveAction);
        }

        private void ApplyMovement(int moveAction)
        {
            if (_bnc == null) return;

            if (_bnc.IsBusy)
            {
                if (_stateManager != null) _stateManager.NotifyMovementInput(false);
                return;
            }

            switch (moveAction)
            {
                case 1:
                    Vector3 fwd = transform.position + transform.forward * (_moveSpeed * Time.deltaTime);
                    _bnc.ApplyMLPosition(fwd);
                    break;
                case 2:
                    transform.Rotate(0f, -_rotationSpeed * Time.deltaTime, 0f);
                    break;
                case 3:
                    transform.Rotate(0f, _rotationSpeed * Time.deltaTime, 0f);
                    break;
                case 4:
                    Vector3 back = transform.position - transform.forward * (_moveSpeed * Time.deltaTime);
                    _bnc.ApplyMLPosition(back);
                    break;
            }

            if (_stateManager != null)
                _stateManager.NotifyMovementInput(moveAction == 1 || moveAction == 4);
        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            var d = actionsOut.DiscreteActions;
            d[0] = 0;
        }
    }
}
