﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

public enum CardState {InDeck, InHand, InDiscard}; 

[System.Serializable]
public class Card : MonoBehaviour
{

    public CardState curState;
    private Board board = Board.me;

    private SpriteRenderer[] cardParts;
    [SerializeField]
    private Sprite[] cardSprites;

    private Sequence tweenSequence;
    private Transform tr;

    public string cardName;
    public int cost;
    public string desc;
    public Sprite cardArt;

    void Awake(){
        tweenSequence = DOTween.Sequence();
        cardParts = GetComponentsInChildren<SpriteRenderer>();
        tr = this.gameObject.transform;
        curState = CardState.InDeck;
    }

    void Start(){
        // card.transform.DOMove(new Vector3(3,3,3),1);
    }

    void Update(){
        cardParts[1].sprite = cardArt;
        // draw card iff in hand
        if(curState == CardState.InHand){
            // turn on each sprite renderer in children then draw the assigned sprite
            for(int i = 0; i < cardParts.Length; i++){
                cardParts[i].enabled = true;
                if(cardSprites[i] != null){
                    cardParts[i].sprite = cardSprites[i];
                }
            }

            // move card into an empty anchor
            for(int i = 0; i < 5; i++){
                Transform anchor = GameObject.Find($"Hand{i}").transform;
                if(anchor.childCount == 0){
                    tr.parent = anchor;
                    tr.DOMove(anchor.position, 1);
                }
            }

        // may require changing depending on implementation
        } else {
            foreach(SpriteRenderer sr in cardParts){
                sr.enabled = false;
            }
        }
    }
    void attack(int amount, Target target){

    }

    void defend(int amount, Target target){

    }

    void OnMouseEnter(){
        tweenSequence.Append(tr.DOScale(1.45f * Vector3.one, .4f).SetId("zoomIn"));
        tweenSequence.Insert(0, tr.DOMoveZ(-1f, .5f).SetId("zoomIn"));
    }

    void OnMouseExit(){
        DOTween.Pause("zoomIn");
        tweenSequence.Append(tr.DOScale(Vector3.one, .1f));
    }

    void OnMouseDown(){
        switch(board.curPhase){
            case Phase.Mulligan:
                curState = CardState.InDiscard;
                tr.parent = board.cardAnchors["Discard Anchor"];
                break;
            case Phase.Play:
                break;
            default:
                Debug.Log("error");
                break;
        }
    }

    
    
}
