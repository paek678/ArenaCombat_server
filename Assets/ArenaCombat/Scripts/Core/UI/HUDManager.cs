using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using ArenaCombat.Core.Combat;
using ArenaCombat.Core.Network;
using ArenaCombat.Core.Skill;

namespace ArenaCombat.Core.UI
{
    public class HUDManager : MonoBehaviour
    {
        public static HUDManager Instance { get; private set; }

        [Header("=== Skill Slots Visual ===")]
        [SerializeField] private float _slotSize = 72f;
        [SerializeField] private float _slotSpacing = 6f;
        [SerializeField] private Color _cooldownOverlayColor = new Color(0f, 0f, 0f, 0.75f);
        [SerializeField] private Color _slotBgColor = new Color(0.12f, 0.12f, 0.12f, 0.9f);
        [SerializeField] private Color _slotBorderColor = new Color(0.8f, 0.7f, 0.3f, 1f);
        [SerializeField] private Color _readyGlowColor = new Color(1f, 1f, 0.6f, 0.4f);

        private Image _playerHPFill;
        private Image _bossHPFill;
        private Text _bossNameLabel;
        private RectTransform _skillSlotContainer;

        private PlayerNetworkController3D _localPlayer;
        private BossNetworkController3D _boss;
        private SkillManager _localSkillMgr;
        private GameStateManager _gsm;

        private readonly List<SkillSlotUI> _slotUIs = new List<SkillSlotUI>();
        private float _refreshTimer;
        private bool _hudActive;
        private bool _uiResolved;
        private int _lastKnownSlotHash;

        private struct SkillSlotUI
        {
            public GameObject Root;
            public Image Background;
            public Image Border;
            public Image IconImage;
            public Image CooldownOverlay;
            public Image ReadyGlow;
            public Text CooldownText;
            public int SlotIndex;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            ResolveUIReferences();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void ResolveUIReferences()
        {
            if (_uiResolved) return;

            var canvas = transform;
            _playerHPFill = FindImageInChildren(canvas, "Player_HPBg");
            _bossHPFill = FindImageInChildren(canvas, "Boss_HPBg");
            _bossNameLabel = FindTextInChildren(canvas, "Boss_NameLabel");

            var slotContainerT = FindDeep(canvas, "SkillSlotContainer");
            if (slotContainerT != null)
                _skillSlotContainer = slotContainerT.GetComponent<RectTransform>();

            _uiResolved = _bossHPFill != null;
        }

        private static Image FindImageInChildren(Transform root, string name)
        {
            var t = FindDeep(root, name);
            return t != null ? t.GetComponent<Image>() : null;
        }

        private static Text FindTextInChildren(Transform root, string name)
        {
            var t = FindDeep(root, name);
            return t != null ? t.GetComponent<Text>() : null;
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                var found = FindDeep(parent.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        private void Update()
        {
            if (!_uiResolved)
            {
                ResolveUIReferences();
                if (!_uiResolved) return;
            }

            if (_gsm == null)
            {
                if (GameStateManager.Instance != null)
                    _gsm = GameStateManager.Instance;
                else
                    return;
            }

            bool shouldBeActive = _gsm.CurrentMatchState == MatchState.InProgress;
            if (shouldBeActive && !_hudActive)
                ActivateHUD();
            else if (!shouldBeActive && _hudActive)
                DeactivateHUD();

            if (!_hudActive) return;

            _refreshTimer += Time.deltaTime;
            if (_refreshTimer < 0.05f) return;
            _refreshTimer = 0f;

            RefreshReferences();
            UpdatePlayerHP();
            UpdateBossHP();
            CheckSlotChangesAndRebuild();
            UpdateSkillSlots();
        }

        private void ActivateHUD()
        {
            _hudActive = true;
            _refreshTimer = 0f;
            _lastKnownSlotHash = 0;
            _localPlayer = null;
            _boss = null;
            _localSkillMgr = null;
            RefreshReferences();
            RebuildSkillSlotUI();
        }

        private void DeactivateHUD()
        {
            _hudActive = false;
            ClearSkillSlotUI();
        }

        private void RefreshReferences()
        {
            if (_localPlayer != null && _boss != null && _localSkillMgr != null) return;

            var players = FindObjectsByType<PlayerNetworkController3D>(FindObjectsSortMode.None);
            foreach (var p in players)
            {
                if (p == null) continue;
                NetworkObject netObj;
                try { netObj = p.NetworkObject; }
                catch { continue; }
                if (netObj == null || !netObj.IsSpawned) continue;
                if (netObj.IsOwner)
                {
                    _localPlayer = p;
                    _localSkillMgr = p.GetComponent<SkillManager>();
                }
            }

            if (_boss == null)
                _boss = FindAnyObjectByType<BossNetworkController3D>();
        }

        private int ComputeSlotHash()
        {
            if (_localSkillMgr == null) return 0;
            var slots = _localSkillMgr.Slots;
            int hash = 17;
            for (int i = 0; i < slots.Count; i++)
                hash = hash * 31 + (slots[i] != null ? slots[i].GetInstanceID() : 0);
            return hash;
        }

        private void CheckSlotChangesAndRebuild()
        {
            int currentHash = ComputeSlotHash();
            if (currentHash != _lastKnownSlotHash)
            {
                _lastKnownSlotHash = currentHash;
                RebuildSkillSlotUI();
            }
        }

        private void UpdatePlayerHP()
        {
            if (_playerHPFill != null && _localPlayer != null)
            {
                float ratio = _localPlayer.MaxHP > 0f
                    ? Mathf.Clamp01(_localPlayer.CurrentHP / _localPlayer.MaxHP)
                    : 0f;
                _playerHPFill.fillAmount = ratio;
            }
        }

        private void UpdateBossHP()
        {
            if (_bossHPFill == null || _boss == null) return;
            var combatant = (ICombatant)_boss;
            _bossHPFill.fillAmount = Mathf.Clamp01(combatant.CurrentHPPercent);
        }

        private void RebuildSkillSlotUI()
        {
            ClearSkillSlotUI();
            if (_skillSlotContainer == null || _localSkillMgr == null) return;

            var slots = _localSkillMgr.Slots;
            int activeCount = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null) activeCount++;
            }

            if (activeCount == 0) return;

            float totalWidth = activeCount * _slotSize + (activeCount - 1) * _slotSpacing;
            float startX = -totalWidth * 0.5f + _slotSize * 0.5f;
            int uiIndex = 0;

            for (int i = 0; i < slots.Count; i++)
            {
                var def = slots[i];
                if (def == null) continue;

                var slotUI = CreateSlotUI(i, def, startX + uiIndex * (_slotSize + _slotSpacing));
                _slotUIs.Add(slotUI);
                uiIndex++;
            }
        }

        private SkillSlotUI CreateSlotUI(int slotIndex, SkillDefinition def, float xPos)
        {
            int layer = gameObject.layer;

            // Root container
            var rootGO = new GameObject($"SkillSlot_{slotIndex}", typeof(RectTransform));
            rootGO.layer = layer;
            rootGO.transform.SetParent(_skillSlotContainer, false);
            var rootRT = rootGO.GetComponent<RectTransform>();
            rootRT.sizeDelta = new Vector2(_slotSize, _slotSize);
            rootRT.anchoredPosition = new Vector2(xPos, 0f);

            // Background panel
            var bgGO = CreateUIChild(rootGO.transform, "Bg", layer);
            var bgImg = bgGO.GetComponent<Image>();
            bgImg.color = _slotBgColor;
            StretchFill(bgGO.GetComponent<RectTransform>(), 0f);

            // Border
            var borderGO = CreateUIChild(rootGO.transform, "Border", layer);
            var borderImg = borderGO.GetComponent<Image>();
            borderImg.color = _slotBorderColor;
            borderImg.type = Image.Type.Sliced;
            StretchFill(borderGO.GetComponent<RectTransform>(), -2f);

            // Inner background (darker, inside border)
            var innerGO = CreateUIChild(rootGO.transform, "Inner", layer);
            var innerImg = innerGO.GetComponent<Image>();
            innerImg.color = new Color(0.05f, 0.05f, 0.05f, 0.95f);
            StretchFill(innerGO.GetComponent<RectTransform>(), -3f);

            // Skill icon
            var iconGO = CreateUIChild(rootGO.transform, "Icon", layer);
            var iconImg = iconGO.GetComponent<Image>();
            iconImg.sprite = def.Icon;
            iconImg.preserveAspect = true;
            if (def.Icon != null)
                iconImg.color = Color.white;
            else
                iconImg.color = GetFallbackColor(def.DisplayName);
            StretchFill(iconGO.GetComponent<RectTransform>(), -4f);

            // Cooldown overlay (filled from bottom)
            var cdGO = CreateUIChild(rootGO.transform, "CDOverlay", layer);
            var cdImg = cdGO.GetComponent<Image>();
            cdImg.color = _cooldownOverlayColor;
            cdImg.type = Image.Type.Filled;
            cdImg.fillMethod = Image.FillMethod.Vertical;
            cdImg.fillOrigin = 0;
            cdImg.fillAmount = 0f;
            StretchFill(cdGO.GetComponent<RectTransform>(), -4f);

            // Ready glow (pulsing border when off cooldown)
            var glowGO = CreateUIChild(rootGO.transform, "ReadyGlow", layer);
            var glowImg = glowGO.GetComponent<Image>();
            glowImg.color = _readyGlowColor;
            StretchFill(glowGO.GetComponent<RectTransform>(), -1f);
            glowGO.SetActive(false);

            // Cooldown text
            var textGO = new GameObject("CDText", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textGO.layer = layer;
            textGO.transform.SetParent(rootGO.transform, false);
            var textRT = textGO.GetComponent<RectTransform>();
            StretchFill(textRT, 0f);
            var cdText = textGO.GetComponent<Text>();
            cdText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            cdText.fontSize = Mathf.RoundToInt(_slotSize * 0.3f);
            cdText.fontStyle = FontStyle.Bold;
            cdText.alignment = TextAnchor.MiddleCenter;
            cdText.color = Color.white;
            cdText.text = "";
            var outline = textGO.AddComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(1f, -1f);

            return new SkillSlotUI
            {
                Root = rootGO,
                Background = bgImg,
                Border = borderImg,
                IconImage = iconImg,
                CooldownOverlay = cdImg,
                ReadyGlow = glowImg,
                CooldownText = cdText,
                SlotIndex = slotIndex,
            };
        }

        private static GameObject CreateUIChild(Transform parent, string name, int layer)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = layer;
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void StretchFill(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-inset, -inset);
            rt.offsetMax = new Vector2(inset, inset);
        }

        private static Color GetFallbackColor(string name)
        {
            if (string.IsNullOrEmpty(name)) return new Color(0.4f, 0.4f, 0.4f, 1f);
            int hash = 0;
            foreach (char c in name) hash = (hash * 397) ^ c;
            float hue = Mathf.Abs(hash % 360) / 360f;
            return Color.HSVToRGB(hue, 0.5f, 0.85f);
        }

        private void UpdateSkillSlots()
        {
            if (_localSkillMgr == null) return;

            float time = Time.time;
            for (int i = 0; i < _slotUIs.Count; i++)
            {
                var ui = _slotUIs[i];
                if (ui.Root == null) continue;

                int slotIdx = ui.SlotIndex;
                float remaining = _localSkillMgr.GetRemainingCooldown(slotIdx);
                var slots = _localSkillMgr.Slots;
                var def = (slotIdx < slots.Count) ? slots[slotIdx] : null;

                if (def == null)
                {
                    ui.Root.SetActive(false);
                    continue;
                }

                float total = def.Cooldown;
                float cdRatio = (total > 0f) ? Mathf.Clamp01(remaining / total) : 0f;
                ui.CooldownOverlay.fillAmount = cdRatio;

                bool isReady = cdRatio <= 0f;

                // Cooldown text
                if (!isReady && remaining > 0.1f)
                {
                    ui.CooldownText.text = remaining >= 10f
                        ? Mathf.CeilToInt(remaining).ToString()
                        : remaining.ToString("F1");
                    ui.CooldownText.enabled = true;
                }
                else
                {
                    ui.CooldownText.enabled = false;
                }

                // Ready glow pulse
                if (isReady)
                {
                    if (!ui.ReadyGlow.gameObject.activeSelf)
                        ui.ReadyGlow.gameObject.SetActive(true);

                    float pulse = 0.2f + 0.2f * Mathf.Sin(time * 3f);
                    var c = _readyGlowColor;
                    c.a = pulse;
                    ui.ReadyGlow.color = c;
                }
                else
                {
                    if (ui.ReadyGlow.gameObject.activeSelf)
                        ui.ReadyGlow.gameObject.SetActive(false);
                }

                // Dim icon when on cooldown
                ui.IconImage.color = isReady
                    ? (def.Icon != null ? Color.white : GetFallbackColor(def.DisplayName))
                    : new Color(0.5f, 0.5f, 0.5f, 1f);
            }
        }

        private void ClearSkillSlotUI()
        {
            foreach (var ui in _slotUIs)
            {
                if (ui.Root != null) Destroy(ui.Root);
            }
            _slotUIs.Clear();
            _lastKnownSlotHash = 0;
        }

        public void ForceRefresh()
        {
            _localPlayer = null;
            _boss = null;
            _localSkillMgr = null;
            _uiResolved = false;
            _lastKnownSlotHash = 0;
        }
    }
}
