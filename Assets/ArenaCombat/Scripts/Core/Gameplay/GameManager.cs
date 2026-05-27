using System.Collections.Generic;
using UnityEngine;
using ArenaCombat.Core.Skill;

// Match-wide reference hub + combat elapsed time tracking.
//
// TEMPORARY BUILDUP-COMPATIBLE REGISTRY:
//   _players / _bosses lists are imported verbatim from Buildup for X2-11
//   SkillManager.FindNearestTarget() compile compatibility. Per
//   BUILDUP_INTEGRATION_PLAN.md the long-term shape is "thin facade" with
//   Players / Bosses queries routed through CombatManager3D / boss registry.
//   X3 (PNC3D ICombatant wiring) and X4 (BNC3D + boss registry) will
//   replace these direct lists with router calls. SkillRegistry / ElapsedTime
//   remain on this facade.
//
// Buildup origin: Assets/Scripts/GameManager.cs

namespace ArenaCombat.Core
{
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Combat objects")]
        // TEMPORARY (see header note) — route via CombatManager3D / boss registry in X3/X4.
        [SerializeField] private List<GameObject> _players = new();
        [SerializeField] private List<GameObject> _bosses  = new();

        [Header("Skill registry")]
        [SerializeField] private SkillRegistry _skillRegistry;

        [Header("Time")]
        private float _elapsedTime;
        private bool  _isRunning;

        // ── External access ─────────────────────────────────────
        public IReadOnlyList<GameObject> Players => _players;
        public IReadOnlyList<GameObject> Bosses  => _bosses;
        public float                     ElapsedTime => _elapsedTime;

        // Single-element convenience accessors.
        public GameObject Player1 => _players.Count > 0 ? _players[0] : null;
        public GameObject Player2 => _players.Count > 1 ? _players[1] : null;
        public GameObject Boss    => _bosses.Count  > 0 ? _bosses[0]  : null;

        // ── Runtime registration ────────────────────────────────
        public void RegisterPlayer(GameObject player)   { if (!_players.Contains(player)) _players.Add(player); }
        public void RegisterBoss  (GameObject boss)     { if (!_bosses.Contains(boss))   _bosses.Add(boss); }
        public void UnregisterPlayer(GameObject player) => _players.Remove(player);
        public void UnregisterBoss  (GameObject boss)   => _bosses.Remove(boss);

        // ══════════════════════════════════════════════════════════
        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
        }

        // ── Read-only SkillRegistry accessor ────────────────────
        public SkillRegistry SkillRegistry => _skillRegistry;

        private void Start()
        {
            // BindAll is local delegate injection (NOT server-only): clients also
            // need SkillDefinition.RuntimeStep populated to satisfy IsReady checks
            // and any future UI cooldown display. Server gating happens at the
            // SkillManager.Update level instead (see SkillManager).
            SkillBinder.BindAll(_skillRegistry);
            StartTimer();
        }

        private void Update()
        {
            if (_isRunning)
                _elapsedTime += Time.deltaTime;
        }

        // ── Timer control ───────────────────────────────────────
        public void StartTimer()
        {
            _elapsedTime = 0f;
            _isRunning   = true;
        }

        public void StopTimer()  => _isRunning = false;
        public void ResumeTimer() => _isRunning = true;
    }
}
