﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using DG.Tweening;

public enum Phase { Mulligan, Play, Resolution };
public class Board : MonoBehaviour {
    private string deckFileName = "deck.json";
    public Phase curPhase;
    public int borrowedTime; // offset time carryover if overplay/underplay
    public int round; // the number of mul-play-res cycles

    // "entity" fields
    public static Board me;
    public GameObject player;
    public GameObject phaseBanner;
    public GameObject[] enemies;
    public GameObject perspectiveCamera;
    public bool actionButtonPressed;
    

    // CARD MANIPULATING FIELDS //
    public GameObject cardPrefab;
    private List<GameObject> pool = new List<GameObject>();
    private Dictionary<string, Sprite> cardArtDict = new Dictionary<string, Sprite>();
    public Dictionary<string, Transform> cardAnchors = new Dictionary<string, Transform>();

    public Queue<GameObject> deck = new Queue<GameObject>();
    public List<GameObject> discard = new List<GameObject>();
    public List<GameObject> hand = new List<GameObject>();
    public int deckCount;

    // MULLIGAN PHASE VARIABLES //
    public int turn; // number of mulligan "sets" completed
    public int mulLimit;
    public List<GameObject> toMul = new List<GameObject>(); // is a subset of `hand`
    public List<GameObject> lockedHand = new List<GameObject>(); // also subset of `hand`, union with `toMul` is equal to `hand`

    // PLAY PHASE VARIABLES //
    public PlaySequence<Action> playSequence = new PlaySequence<Action>();

    //Particle Systems
    public ParticleSystem TimelineResolutionPS;

    // FMOD variables
    [FMODUnity.EventRef]
    public string lockSoundEvent;
    [FMODUnity.EventRef]
    public string toPlayPhaseSoundEvent;
    [FMODUnity.EventRef]
    public string toResolutionPhaseSoundEvent;
    [FMODUnity.EventRef]
    public string toMulliganPhaseSoundEvent;

    FMOD.Studio.EventInstance lockSound;
    FMOD.Studio.EventInstance toPlayPhaseSound;
    FMOD.Studio.EventInstance toResolutionPhaseSound;
    FMOD.Studio.EventInstance toMulliganPhaseSound;

    [System.Serializable]
    public class DeckList {
        public List<CardData> deckList;
        public DeckList(){
            deckList = new List<CardData>();
        }
    }
    
    [System.Serializable]
    public class CardData {
        public string cardName;
        public int cost;
        public string desc;
        public string artPath;
        public string[] cardProps;
    }

    // Action describes an action that can be enqueued during the play phase.
    // This includes the card to be played for the action, its target(s), and
    // the time cost of the action.

    // Extension of List, used to model the sequence of actions created
    // during the play phase, and executed during the resolution phase.
    public class PlaySequence<T> : List<T> {
        public int totalTime;

        public PlaySequence() {
            totalTime = 0;
        }

        public int IndexOfCompleteTime(int targetTime) {
            for(int i = 0; i < this.Count; i++) {
                Action action = this[i] as Action;
                if(action.completeTime == targetTime) return i; 
                else if (action.completeTime > targetTime) return i-1;
            }
            return -1;
        }

        // adjusts the completeTime by amount speicfied in `offset` for each action starting with the specified `index`
        // this is used when dequeueing actions during play phase, and during resolution phase
        public void RecalculateCompleteTime(int index, int offset) {
            for(int i = index; i < this.Count; i++) {
                Action action = this[i] as Action;
                action.completeTime -= offset; 
            }
        }
        
        // helper function for enqueueing enemy actions between play and resolution phase
        public bool ContainsEnemyAction() {
            foreach(T entry in this) {
                if(entry is EnemyAction) return true;
            }
            return false;
        }

        public int GetLastEnemyActionTime() {
            int finalTime = 0;
            foreach(T entry in this) {
                if(entry is EnemyAction) {
                    EnemyAction enemyAction = entry as EnemyAction;
                    finalTime = enemyAction.completeTime;
                }
            }
            return finalTime;
        }

        public new void Add(T item) {
            base.Add(item);
            if(item.GetType() == typeof(PlayerAction)) {
                PlayerAction action = item as PlayerAction;
                this.totalTime += action.card.cost;
            }
        }

        public new void Remove(T item) {
            if(item.GetType() == typeof(PlayerAction)) {
                PlayerAction action = item as PlayerAction;
                int idx = this.IndexOfCompleteTime(action.completeTime);
                if(idx != -1) this.RecalculateCompleteTime(idx, action.card.cost);
                this.totalTime -= action.card.cost;
                action.card.curState = CardState.InHand;
                Board.me.Mulligan(action.card); // jank
                Destroy(action.instance);
            } else if (item is EnemyAction) {
                EnemyAction action = item as EnemyAction;
                Destroy(action.instance);
            }
            base.Remove(item);
        }

        public void DequeuePlayerAction(T item) {
            if(item.GetType() == typeof(PlayerAction)) {
                PlayerAction action = item as PlayerAction;
                int idx = this.IndexOfCompleteTime(action.completeTime);
                if(idx != -1) this.RecalculateCompleteTime(idx, action.card.cost);
                this.totalTime -= action.card.cost;
                action.card.curState = CardState.InHand;
                Destroy(action.instance);
            }
            base.Remove(item);
        
        }

        public new string ToString() {
            string retStr = "";
            foreach(T entry in this) {
                if(entry is PlayerAction) {
                    PlayerAction action = entry as PlayerAction;
                    retStr += $"Player action of {action.card.cardName} at time {action.completeTime}\n";
                } else if (entry is EnemyAction) {
                    EnemyAction action = entry as EnemyAction;
                    retStr += $"Enemy action of {action.actionType} at time {action.completeTime}\n";
                }
            }

            return retStr;
        }

        
    }
    
    public void Mulligan(Card card) {
        if(hand.Contains(card.gameObject)) {
            card.isSettled = false;
            card.curState = CardState.InDiscard;
            discard.Add(card.gameObject);
            hand.Remove(card.gameObject);
            card.transform.parent = cardAnchors["Discard Anchor"];
        } else {
            Debug.LogError("attempted to discard card that was not in hand");
        }
    }
    
    public void DrawCard() {
        if(deck.Count == 0) Reshuffle();
        
        GameObject curCard = deck.Dequeue();
        curCard.GetComponent<Card>().curState = CardState.InHand;
        hand.Add(curCard);
        // find empty hand anchor
        for(int i = 0; i < 5; i++) {
            Transform anchor = GameObject.Find($"Hand{i}").transform;
                if(anchor.childCount == 0){
                    curCard.transform.parent = anchor;
                    curCard.GetComponent<Card>().isSettled = false;
                }
        }
    }

    public void Reshuffle() {
        discard = FisherYatesShuffle(discard);
        foreach(GameObject card in discard) {
            deck.Enqueue(card);
            Card curCard = card.GetComponent<Card>();
            curCard.isSettled = false;
            curCard.curState = CardState.InDeck;
            curCard.transform.parent = cardAnchors["Deck Anchor"];
        }
        discard.Clear();
    }

    public IEnumerator ResetEnemySprites() {
        yield return new WaitForSeconds(.5f);
        foreach(GameObject enemy in enemies) {
            enemy.GetComponent<SpriteRenderer>().sprite = enemy.GetComponent<Enemy>().combatStates[0];
            // enemy.transform.position = enemy.GetComponent<Target>().startPos;
        }
    }

    public IEnumerator ResetPlayerSprites() {
        yield return new WaitForSeconds(.5f);
        player.GetComponent<SpriteRenderer>().sprite = player.GetComponent<Player>().combatStates[0];
        // player.transform.position = player.GetComponent<Target>().startPos;
    }

    public IEnumerator ResetActionCamera() {
        yield return new WaitForSeconds(.5f);
        perspectiveCamera.transform.DOLocalMove(new Vector3(0, 0, 2), .5f);
    }

    public IEnumerator ExecuteAction(PlaySequence<Action> playSequence) {
        if(playSequence.Count == 0) {
            yield return new WaitForSeconds(1f);
        }

        // move actors closer together (resets at end of coroutine)
        player.transform.DOMoveX(-4.5f, .5f);
        enemies[0].transform.DOMoveX(4.5f, .5f);
        // GameObject.Find("Main Camera").GetComponent<Camera>().cullingMask = 0;

        while(playSequence.Count != 0) {
            switch(playSequence[0].GetType().ToString()) {
                case "PlayerAction":
                    PlayerAction playerAction = playSequence[0] as PlayerAction;
                    playerAction.resolveAction();

                    // anims
                    TimelineResolutionPS.Play();
                    playSequence.Remove(playSequence[0]);
                    perspectiveCamera.transform.DOLocalMove(new Vector3(-3.5f, 0, 8), .5f);
                    
                    // player.transform.position = new Vector3(-3, player.transform.position.y, player.transform.position.z);
                    // enemies[0].transform.position = new Vector3(3, enemies[0].transform.position.y, enemies[0].transform.position.z);

                    // player.transform.DOMoveX(-1.6f, .5f).SetEase(Ease.OutExpo);
                    // enemies[0].transform.DOMoveX(1.6f, .5f).SetEase(Ease.OutExpo);
                    
                    // TODO: abstract this out
                    player.GetComponent<SpriteRenderer>().sprite = playerAction.card.cardProps[0] == "Attack" ? player.GetComponent<Player>().combatStates[1] : player.GetComponent<Player>().combatStates[2];
                    if(playerAction.card.cardProps[0] == "Defend") {
                        player.GetComponent<ParticleSystem>().Play();
                    }
                    yield return new WaitForSeconds(.2f);

                    // StartCoroutine(ResetActionCamera());
                    StartCoroutine(ResetPlayerSprites());
                    yield return new WaitForSeconds(1.5f);
                    break;
                
                case "EnemyAction":
                    EnemyAction enemyAction = playSequence[0] as EnemyAction;
                    enemyAction.resolveAction();
                    
                    // anims
                    playSequence.Remove(playSequence[0]);
                    perspectiveCamera.transform.DOLocalMove(new Vector3(3.5f, 0, 8), .5f);
                    
                    // player.transform.position = new Vector3(-3, player.transform.position.y, player.transform.position.z);
                    // enemies[0].transform.position = new Vector3(3, enemies[0].transform.position.y, enemies[0].transform.position.z);
                    // player.transform.DOMoveX(-1.6f, .5f).SetEase(Ease.OutExpo);
                    // enemies[0].transform.DOMoveX(1.6f, .5f).SetEase(Ease.OutExpo);

                    if(enemyAction.actionType == ActionType.Defense) {
                        enemyAction.owner.GetComponent<ParticleSystem>().Play();
                    }
                    enemyAction.owner.GetComponent<SpriteRenderer>().sprite = enemyAction.owner.GetComponent<Enemy>().combatStates[(int)enemyAction.actionType + 1];
                    yield return new WaitForSeconds(.2f);

                    StartCoroutine(ResetEnemySprites());
                    yield return new WaitForSeconds(1.5f);
                    break;
            }
        }
        player.transform.DOMoveX(-10, .5f);
        enemies[0].transform.DOMoveX(10, .5f);
        EnableUIView();

        if(borrowedTime != 0) GameObject.Find("HourglassGlow").GetComponent<HourglassGlow>().isActive = true;
    }

    private void MulToPlayPhase() {  
        phaseBanner.GetComponent<PhaseBanner>().phaseName.text = "Play Phase";
        phaseBanner.GetComponent<PhaseBanner>().canBanner = true;
        phaseBanner.GetComponent<PhaseBanner>().doBanner();

        lockedHand.Clear();
        turn = 0;
        // FMOD Play Phase Transition Sound           
        toPlayPhaseSound.start();
        curPhase = Phase.Play;
    }

    private void ResToMulPhase() {
        mulLimit = 4;
        round++;

        phaseBanner.GetComponent<PhaseBanner>().phaseName.text = "Mulligan Phase"; 
        phaseBanner.GetComponent<PhaseBanner>().canBanner = true;
        phaseBanner.GetComponent<PhaseBanner>().doBanner();

        perspectiveCamera.transform.DOLocalMove(new Vector3(0, 0, 2), .5f);

        // reset block values
        player.GetComponent<Target>().block = 0;
        foreach(GameObject enemy in enemies) enemy.GetComponent<Target>().block = 0;

        // FMOD Mulligan Phase Transition Sound
        toMulliganPhaseSound.start();
        curPhase = Phase.Mulligan;
    }
    
    private void GetAnchors() {
        cardAnchors.Add("Deck Anchor", GameObject.Find("_DeckAnchor").transform);
        for(int i = 0; i < 5; i++){
            cardAnchors.Add($"Hand {i}", GameObject.Find($"Hand{i}").transform);
        }
        cardAnchors.Add("Discard Anchor", GameObject.Find("_DiscardAnchor").transform);
    }

    private DeckList LoadDeckData(){
        string path = Path.Combine(Application.streamingAssetsPath, deckFileName);
        
        if(File.Exists(path)){
            string data = File.ReadAllText(path);
            DeckList parsed = JsonUtility.FromJson<DeckList>(data);
            return parsed;
        } else {
            Debug.Log("ERROR: Failed to read deck data from json");
            return new DeckList();
        }
    }

    private void LoadCardArt(string path, string cardName) {
        WWW www = new WWW(path);
        cardArtDict[cardName] = Sprite.Create(www.texture, new Rect(0, 0, www.texture.width, www.texture.height), new Vector2(0.5f, 0.5f));
    }

    private List<GameObject> FisherYatesShuffle(List<GameObject> list) {
        for (int i = 0; i < list.Count; i++) {
            GameObject temp = list[i];
            int randIdx = Random.Range(i, list.Count);
            list[i] = list[randIdx];
            list[randIdx] = temp;
        }
        return list;
    }

    void Awake(){
        me=this;
    }

    void Start(){
        player = GameObject.Find("Player");
        enemies = GameObject.FindGameObjectsWithTag("Enemy");
        phaseBanner = GameObject.Find("PhaseBanner");
        perspectiveCamera = GameObject.Find("Perspective Camera");

        List<CardData> deckList = LoadDeckData().deckList;
        GetAnchors(); // get anchor positions

        // initialize phase variables
        curPhase = Phase.Mulligan;
        mulLimit = 4;
        turn = 0;

        if(turn == 0) borrowedTime = 0;
        round = 0;
        
        foreach(CardData card in deckList){
            // load card art into dictionary
            string path = "file://" + Path.Combine(Application.streamingAssetsPath, card.artPath);
            LoadCardArt(path, card.cardName);

            // create card gameobject and populate its properties
            GameObject curCard = Instantiate(cardPrefab, cardAnchors["Deck Anchor"].position, Quaternion.identity);
            curCard.transform.parent = cardAnchors["Deck Anchor"];
            Card cardScript = curCard.GetComponent<Card>();
            cardScript.cardName = card.cardName;
            cardScript.cost = card.cost;
            cardScript.desc = card.desc;
            cardScript.cardProps = card.cardProps;
            cardScript.cardArt = cardArtDict[cardScript.cardName]; 
            pool.Add(curCard);
        }
        pool = FisherYatesShuffle(pool);
        
        // now that all the preloading is done, actually put cards into the deck
        foreach(GameObject card in pool) {
            deck.Enqueue(card);
        }

        // FMOD object init
        lockSound = FMODUnity.RuntimeManager.CreateInstance(lockSoundEvent);
        toPlayPhaseSound = FMODUnity.RuntimeManager.CreateInstance(toPlayPhaseSoundEvent);
        toResolutionPhaseSound = FMODUnity.RuntimeManager.CreateInstance(toResolutionPhaseSoundEvent);
        toMulliganPhaseSound = FMODUnity.RuntimeManager.CreateInstance(toMulliganPhaseSoundEvent);
    }

    void Update(){
        actionButtonPressed = GameObject.FindObjectOfType<ActionButton>().buttonPressed;
        deckCount = deck.Count; // exposes variable for debug
        switch(curPhase){
            case Phase.Mulligan:
                StartCoroutine(ResetEnemySprites());
                StartCoroutine(ResetPlayerSprites());
                

                while(hand.Count < 5){
                    DrawCard();
                }

                if(lockedHand.Count == 5 || mulLimit == 0) {
                    if(!IsInvoking()) Invoke("MulToPlayPhase", .7f);
                    
                } else if(Input.GetKeyDown(KeyCode.E) || actionButtonPressed) {
                    turn++;
                    foreach(GameObject card in hand) {
                        if(!toMul.Contains(card) && !lockedHand.Contains(card)) {
                            lockedHand.Add(card);
                            // FMOD Play Lock Sound
                            lockSound.start();
                        }
                    }

                    foreach(GameObject card in toMul) {
                        Mulligan(card.GetComponent<Card>()); 
                    }
                    mulLimit = Mathf.Min(4 - turn, 4 - lockedHand.Count);
                    toMul.Clear();
                    GameObject.FindObjectOfType<ActionButton>().buttonPressed = false;
                    
                }
                break;
            case Phase.Play:
                if(Input.GetKeyDown(KeyCode.E) || actionButtonPressed) {
                    // discard the cards that were not enqueue'd
                    foreach(GameObject card in hand) {
                        if(card.GetComponent<Card>().curState != CardState.InQueue) {
                            toMul.Add(card);
                        }
                    }
                    foreach(GameObject card in toMul) {
                        Mulligan(card.GetComponent<Card>()); 
                    }
                    toMul.Clear();

                    curPhase = Phase.Resolution;

                    // FMOD Resolution Phase Transition Sound
                    toResolutionPhaseSound.start();
                    // enqueue enemy actions
                    if (!playSequence.ContainsEnemyAction()) {
                        foreach(GameObject enemy in enemies) {
                            Enemy enemyScript = enemy.GetComponent<Enemy>();
                            foreach(EnemyAction actionToAdd in enemyScript.curActions) {
                                if(!playSequence.Contains(actionToAdd)) {
                                    int idx = playSequence.IndexOfCompleteTime(actionToAdd.completeTime);
                                    if(actionToAdd.completeTime == 0) {
                                        playSequence.Insert(0, actionToAdd);
                                    } else if(idx != -1) {
                                        playSequence.Insert(idx + 1, actionToAdd); // insert AFTER given index to give player priority in resolution
                                    } else {
                                        playSequence.Add(actionToAdd); // add to end if the scheduled play time is after the last player action
                                    }
                                }
                            }
                            enemyScript.prevActions = enemyScript.curActions;
                            enemyScript.curActions.Clear();
                        }
                    }
                    Debug.Log($"Play sequence is: \n{playSequence.ToString()}");
                    
                    // calculate borrowed time for next round                    
                    borrowedTime = playSequence.totalTime - playSequence.GetLastEnemyActionTime();
                    GameObject.FindObjectOfType<ActionButton>().buttonPressed = false;
                    StartCoroutine(ExecuteAction(playSequence)); // resolve all enqueued actions
                }
                
                break;

            case Phase.Resolution:
                // waits for ExecuteAction coroutine to finish
                if(playSequence.Count == 0) {
                    if(!IsInvoking()) Invoke("ResToMulPhase", .7f);
                }
                break;
        }
            
    }
}
