using System;
using System.Collections;
using System.Collections.Generic;
using ArenaCombat.Core.Network;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class CardManager : MonoBehaviour
{
    [Serializable]
    private sealed class DraftSideUIBinding
    {
        [Tooltip("Optional root object for this side UI.")]
        public GameObject uiRoot = null;

        [Tooltip("Persistent selected-card slots for this side. Expected size: 4.")]
        public Image[] slots = new Image[4];
    }

    [Header("Card Data/UI")]
    public AbilityCard[] allCards;
    public CardUI[] cardSlots;
    public GameObject cardUIPanel;
    public Canvas mainCanvas;

    [Header("Persistent Card Slots")]
    [SerializeField] private DraftSideUIBinding hostUI = new DraftSideUIBinding();
    [SerializeField] private DraftSideUIBinding clientUI = new DraftSideUIBinding();
    [SerializeField] private Image bigCardPreview;

    [Header("Network Draft")]
    [SerializeField] private bool useNetworkSynchronizedDraft = true;
    [SerializeField] private bool autoBindToGameStateManager = true;

    [Header("Standalone Fallback")]
    [SerializeField] private float standaloneFirstDraftDelay = 10f;
    [SerializeField] private float standaloneDraftInterval = 10f;
    [SerializeField] private bool pauseTimeScaleInStandalone = true;
    [SerializeField] private int standaloneMaxSelections = 4;

    [Header("Runtime Debug (Read-Only)")]
    [SerializeField] private ulong debugHostClientId = ulong.MaxValue;
    [SerializeField] private ulong debugGuestClientId = ulong.MaxValue;
    [SerializeField] private int debugActiveDraftRound = -1;

    private readonly List<AbilityCard> standaloneSelectedCards = new List<AbilityCard>();
    private readonly Dictionary<int, AbilityCard> cardByIndex = new Dictionary<int, AbilityCard>();
    private readonly Dictionary<AbilityCard, int> indexByCard = new Dictionary<AbilityCard, int>();
    private readonly Dictionary<AbilityCard, int> currentOfferLookup = new Dictionary<AbilityCard, int>();

    private bool subscribed;
    private bool networkModeActive;
    private bool localSelectionSubmitted;
    private int activeDraftRound = -1;
    private int[] localOfferIndices;
    private ulong hostClientId = ulong.MaxValue;
    private ulong guestClientId = ulong.MaxValue;

    private void Start()
    {
        BuildCardIndexLookup();

        if (cardUIPanel != null)
        {
            cardUIPanel.SetActive(false);
        }

        InitializePersistentSlotsUI();

        if (autoBindToGameStateManager)
        {
            TryBindNetworkEvents();
        }

        if (!networkModeActive)
        {
            ScheduleStandaloneDraft(standaloneFirstDraftDelay);
        }
    }

    private void Update()
    {
        if (!networkModeActive && useNetworkSynchronizedDraft && autoBindToGameStateManager)
        {
            TryBindNetworkEvents();
        }
    }

    private void OnDestroy()
    {
        UnbindNetworkEvents();

        if (!networkModeActive && pauseTimeScaleInStandalone && Time.timeScale == 0f)
        {
            Time.timeScale = 1f;
        }
    }

    public void ShowCardSelection()
    {
        if (networkModeActive)
        {
            return;
        }

        StartStandaloneDraftNow();
    }

    public void OnCardSelected(AbilityCard card)
    {
        if (card == null)
        {
            return;
        }

        if (networkModeActive)
        {
            HandleNetworkCardSelected(card);
            return;
        }

        HandleStandaloneCardSelected(card);
    }

    private void TryBindNetworkEvents()
    {
        if (!useNetworkSynchronizedDraft || subscribed)
        {
            return;
        }

        GameStateManager gsm = GameStateManager.Instance;
        if (gsm == null || !gsm.IsSpawned)
        {
            return;
        }

        gsm.OnCardDraftStarted += HandleNetworkCardDraftStarted;
        gsm.OnCardDraftEnded += HandleNetworkCardDraftEnded;
        gsm.OnCardSelectionResolved += HandleNetworkCardSelectionResolved;
        gsm.OnCardSelectionRejected += HandleNetworkCardSelectionRejected;

        subscribed = true;
        networkModeActive = true;
        CancelInvoke(nameof(ShowCardSelection));

        gsm.RegisterCardCatalogSize(allCards != null ? allCards.Length : 0);
        RefreshParticipantsAndHistoryFromGameState();

        if (gsm.IsGlobalCardDraftActive)
        {
            HandleNetworkCardDraftStarted(gsm.CurrentCardDraftRound, gsm.CurrentCardDraftTimer);
        }
    }

    private void UnbindNetworkEvents()
    {
        if (!subscribed)
        {
            return;
        }

        GameStateManager gsm = GameStateManager.Instance;
        if (gsm != null)
        {
            gsm.OnCardDraftStarted -= HandleNetworkCardDraftStarted;
            gsm.OnCardDraftEnded -= HandleNetworkCardDraftEnded;
            gsm.OnCardSelectionResolved -= HandleNetworkCardSelectionResolved;
            gsm.OnCardSelectionRejected -= HandleNetworkCardSelectionRejected;
        }

        subscribed = false;
    }

    private void HandleNetworkCardDraftStarted(int round, float duration)
    {
        activeDraftRound = round;
        debugActiveDraftRound = round;
        localSelectionSubmitted = false;
        RefreshParticipantsAndHistoryFromGameState();

        if (cardUIPanel != null && !cardUIPanel.activeSelf)
        {
            cardUIPanel.SetActive(true);
        }

        GameStateManager gsm = GameStateManager.Instance;
        if (gsm == null || !gsm.TryGetLocalCardDraftOffer(out int[] offerIndices) || offerIndices == null)
        {
            localOfferIndices = null;
            currentOfferLookup.Clear();
            if (cardUIPanel != null)
            {
                cardUIPanel.SetActive(false);
            }
            return;
        }

        ulong localClientId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : ulong.MaxValue;
        if (GetHistoryCount(localClientId) >= gsm.MaxCardDraftRounds)
        {
            localOfferIndices = null;
            currentOfferLookup.Clear();
            if (cardUIPanel != null)
            {
                cardUIPanel.SetActive(false);
            }
            return;
        }

        localOfferIndices = offerIndices;
        PopulateCardSlotsFromOffer(offerIndices);

        Debug.Log($"[CardManager] Draft started. round={round}, duration={duration:0.00}s");
    }

    private void HandleNetworkCardDraftEnded(int round)
    {
        if (activeDraftRound != -1 && round != activeDraftRound)
        {
            return;
        }

        activeDraftRound = -1;
        debugActiveDraftRound = -1;
        localSelectionSubmitted = false;
        localOfferIndices = null;
        currentOfferLookup.Clear();
        StartCoroutine(HideCardPanelAnimated());
    }

    private void HandleNetworkCardSelectionResolved(ulong playerId, int slotIndex, int cardIndex)
    {
        RefreshParticipantsAndHistoryFromGameState();
        ApplyPersistentSelectionIcon(playerId, slotIndex, cardIndex);

        if (NetworkManager.Singleton != null && playerId == NetworkManager.Singleton.LocalClientId)
        {
            localSelectionSubmitted = false;
            localOfferIndices = null;
            currentOfferLookup.Clear();
            StartCoroutine(HideCardPanelAnimated());
        }
    }

    private void HandleNetworkCardSelectionRejected(int round, int requestedCardIndex, string reason)
    {
        if (round != activeDraftRound)
        {
            return;
        }

        localSelectionSubmitted = false;
        if (localOfferIndices != null)
        {
            if (cardUIPanel != null && !cardUIPanel.activeSelf)
            {
                cardUIPanel.SetActive(true);
            }

            PopulateCardSlotsFromOffer(localOfferIndices);
        }

        Debug.LogWarning($"[CardManager] Selection rejected. round={round}, card={requestedCardIndex}, reason={reason}");
    }
    private void HandleNetworkCardSelected(AbilityCard card)
    {
        if (localSelectionSubmitted || activeDraftRound < 0)
        {
            return;
        }

        if (!currentOfferLookup.TryGetValue(card, out int cardIndex))
        {
            return;
        }

        GameStateManager gsm = GameStateManager.Instance;
        if (gsm == null)
        {
            return;
        }

        localSelectionSubmitted = true;
        gsm.SubmitLocalCardSelection(activeDraftRound, cardIndex);
    }

    private void HandleStandaloneCardSelected(AbilityCard card)
    {
        if (standaloneSelectedCards.Count >= standaloneMaxSelections)
        {
            return;
        }

        standaloneSelectedCards.Add(card);
        int slotIndex = standaloneSelectedCards.Count - 1;

        Image[] hostSlots = GetHostSlots();
        if (slotIndex >= 0 && slotIndex < hostSlots.Length)
        {
            SetSlotSprite(hostSlots[slotIndex], card.cardIcon);
        }

        StartCoroutine(HideStandaloneAfterSelection());
    }

    private IEnumerator HideStandaloneAfterSelection()
    {
        yield return HideCardPanelAnimated();

        if (pauseTimeScaleInStandalone)
        {
            Time.timeScale = 1f;
        }

        if (standaloneSelectedCards.Count < standaloneMaxSelections)
        {
            ScheduleStandaloneDraft(standaloneDraftInterval);
        }
    }

    private IEnumerator HideCardPanelAnimated()
    {
        if (cardSlots != null)
        {
            foreach (CardUI slot in cardSlots)
            {
                if (slot == null)
                {
                    continue;
                }

                // Draft offers can disable some choice objects (e.g. Choice2/Choice3).
                // Starting a coroutine on an inactive GameObject throws an error.
                if (!slot.gameObject.activeInHierarchy || !slot.isActiveAndEnabled)
                {
                    continue;
                }

                StartCoroutine(slot.PlayDisappearAnimation());
            }

            yield return new WaitForSecondsRealtime(1f);
        }

        if (cardUIPanel != null)
        {
            cardUIPanel.SetActive(false);
        }
    }

    private void StartStandaloneDraftNow()
    {
        if (standaloneSelectedCards.Count >= standaloneMaxSelections)
        {
            return;
        }

        if (cardUIPanel != null && !cardUIPanel.activeSelf)
        {
            cardUIPanel.SetActive(true);
        }

        PopulateCardSlotsRandomlyStandalone();

        if (pauseTimeScaleInStandalone)
        {
            Time.timeScale = 0f;
        }
    }

    private void PopulateCardSlotsRandomlyStandalone()
    {
        currentOfferLookup.Clear();

        if (allCards == null || allCards.Length == 0 || cardSlots == null || cardSlots.Length == 0)
        {
            return;
        }

        List<AbilityCard> pool = new List<AbilityCard>(allCards);
        for (int i = 0; i < cardSlots.Length; i++)
        {
            CardUI slot = cardSlots[i];
            if (slot == null)
            {
                continue;
            }

            if (pool.Count == 0)
            {
                slot.gameObject.SetActive(false);
                continue;
            }

            int pick = UnityEngine.Random.Range(0, pool.Count);
            AbilityCard card = pool[pick];
            pool.RemoveAt(pick);

            slot.gameObject.SetActive(true);
            slot.Setup(card, this);

            if (indexByCard.TryGetValue(card, out int idx))
            {
                currentOfferLookup[card] = idx;
            }
        }
    }

    private void PopulateCardSlotsFromOffer(int[] offerIndices)
    {
        currentOfferLookup.Clear();

        if (cardSlots == null)
        {
            return;
        }

        for (int i = 0; i < cardSlots.Length; i++)
        {
            CardUI slot = cardSlots[i];
            if (slot == null)
            {
                continue;
            }

            int cardIndex = (offerIndices != null && i < offerIndices.Length) ? offerIndices[i] : -1;
            if (!cardByIndex.TryGetValue(cardIndex, out AbilityCard card) || card == null)
            {
                slot.gameObject.SetActive(false);
                continue;
            }

            slot.gameObject.SetActive(true);
            slot.Setup(card, this);
            currentOfferLookup[card] = cardIndex;
        }
    }

    private void RefreshParticipantsAndHistoryFromGameState()
    {
        GameStateManager gsm = GameStateManager.Instance;
        if (gsm == null)
        {
            return;
        }

        if (gsm.TryGetCurrentDraftParticipants(out ulong host, out ulong guest))
        {
            hostClientId = host;
            guestClientId = guest;
            debugHostClientId = host;
            debugGuestClientId = guest;
        }

        InitializePersistentSlotsUI();

        if (hostClientId != ulong.MaxValue && gsm.TryGetPlayerCardHistory(hostClientId, out IReadOnlyList<int> hostHistory))
        {
            for (int i = 0; i < hostHistory.Count && i < 4; i++)
            {
                ApplyPersistentSelectionIcon(hostClientId, i, hostHistory[i]);
            }
        }

        if (guestClientId != ulong.MaxValue && gsm.TryGetPlayerCardHistory(guestClientId, out IReadOnlyList<int> guestHistory))
        {
            for (int i = 0; i < guestHistory.Count && i < 4; i++)
            {
                ApplyPersistentSelectionIcon(guestClientId, i, guestHistory[i]);
            }
        }
    }

    private int GetHistoryCount(ulong clientId)
    {
        GameStateManager gsm = GameStateManager.Instance;
        if (gsm == null || clientId == ulong.MaxValue)
        {
            return 0;
        }

        return gsm.TryGetPlayerCardHistory(clientId, out IReadOnlyList<int> history) ? history.Count : 0;
    }

    private void ApplyPersistentSelectionIcon(ulong playerId, int slotIndex, int cardIndex)
    {
        if (slotIndex < 0 || slotIndex >= 4)
        {
            return;
        }

        if (!cardByIndex.TryGetValue(cardIndex, out AbilityCard card) || card == null)
        {
            return;
        }

        Image[] targetSlots = Array.Empty<Image>();
        if (playerId == hostClientId)
        {
            targetSlots = GetHostSlots();
        }
        else if (playerId == guestClientId)
        {
            targetSlots = GetClientSlots();
        }

        if (slotIndex >= targetSlots.Length)
        {
            return;
        }

        SetSlotSprite(targetSlots[slotIndex], card.cardIcon);
    }

    private void InitializePersistentSlotsUI()
    {
        Image[] hostSlots = GetHostSlots();
        for (int i = 0; i < hostSlots.Length; i++)
        {
            ClearSlot(hostSlots[i]);
        }

        Image[] clientSlots = GetClientSlots();
        for (int i = 0; i < clientSlots.Length; i++)
        {
            ClearSlot(clientSlots[i]);
        }
    }

    private Image[] GetHostSlots()
    {
        if (hostUI != null && hostUI.slots != null && hostUI.slots.Length > 0)
        {
            return hostUI.slots;
        }

        return Array.Empty<Image>();
    }

    private Image[] GetClientSlots()
    {
        if (clientUI != null && clientUI.slots != null)
        {
            return clientUI.slots;
        }

        return Array.Empty<Image>();
    }

    private void SetSlotSprite(Image slot, Sprite sprite)
    {
        if (slot == null)
        {
            return;
        }

        slot.sprite = sprite;
        slot.enabled = true;

        IconHover hover = slot.GetComponent<IconHover>();
        if (hover != null && bigCardPreview != null)
        {
            hover.Setup(sprite, bigCardPreview);
        }
    }

    private static void ClearSlot(Image slot)
    {
        if (slot == null)
        {
            return;
        }

        slot.enabled = false;
        slot.sprite = null;
    }

    private void BuildCardIndexLookup()
    {
        cardByIndex.Clear();
        indexByCard.Clear();

        if (allCards == null)
        {
            return;
        }

        for (int i = 0; i < allCards.Length; i++)
        {
            AbilityCard card = allCards[i];
            if (card == null)
            {
                continue;
            }

            cardByIndex[i] = card;
            if (!indexByCard.ContainsKey(card))
            {
                indexByCard.Add(card, i);
            }
        }
    }

    private void ScheduleStandaloneDraft(float delay)
    {
        CancelInvoke(nameof(ShowCardSelection));
        Invoke(nameof(ShowCardSelection), Mathf.Max(0.1f, delay));
    }
}
