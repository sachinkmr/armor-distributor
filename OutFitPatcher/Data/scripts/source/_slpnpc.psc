Scriptname _SLPNPC extends activemagiceffect  


import debug
import utility
Import Actor

;====================================================================================

form Head = none
form Chest = none
form Boots = none
form Hands = none
form Cloak = none
form Backpack = none
form slot44 = none
form slot45 = none
form slot48 = none
form slot49 = none
form slot52 = none
form slot53 = none
form slot54 = none
form slot55 = none
form slot56 = none
form slot57 = none
form slot58 = none
form slot59 = none
form slot60 = none
;armor slot46 = none already done as cloak
;armor slot47 = none already done as backpack
form ShieldObj = none
form WeaponObj = none

_SLPQuestScript Property _SLPQuest Auto

Actor targ

Int Snore
Int iRandomIndex


;====================================================================================

Event OnEffectStart(Actor akTarget, Actor akCaster)
    targ = akTarget
    If targ.GetFactionReaction(_SLPQuest.PlayerRef) == 1 || targ.IsInFaction(_SLPQuest.BanditFaction)
        If _SLPQuest.SleepHostile == 2
            GoToState("Bandit")
        ElseIf _SLPQuest.SleepHostile == 1
            GoToState("Naked")
        ElseIf _SLPQuest.SleepHostile == 3
            If Utility.RandomInt(0, 1) == 0
                GoToState("Bandit")
            Else
                GoToState("Naked")
            EndIf
        Else
            GoToState("Snore")
        EndIf
    Else
        If _SLPQuest.Sleepwear == 2
            GoToState("Robe")
        ElseIf _SLPQuest.Sleepwear == 1
            GoToState("Naked")
        ElseIf _SLPQuest.Sleepwear == 3
            If Utility.RandomInt(0, 1) == 0
                GoToState("Robe")
            Else
                GoToState("Naked")
            EndIf
        Else
            GoToState("Snore")
        EndIf
    EndIf
EndEvent

;====================================================================================

State Naked

    Event OnBeginState()
        If targ
            If _SLPQuest.SKSEInstalled

                UndressForBed()
                Chest = targ.GetWornForm(0x00000004)
                if Chest
                    targ.UnequipItem(Chest)
                endIf

            EndIf

            Snore = _SLPQuest._SLPSnoreLP.Play(targ)
            Sound.SetInstanceVolume(Snore, _SLPQuest.SnoreVol)
        EndIf
    EndEvent

    Event OnCellDetach()
        OnUnload()
    EndEvent

    Event OnUnload()
        If targ
            NoMoreNaked()
        endIf
        Sound.StopInstance(Snore)
    EndEvent

    Event OnDetachedFromCell()
        OnUnload()
    EndEvent

    Event OnEffectFinish(Actor akTarget, Actor akCaster)
        OnUnload()
    EndEvent

EndState

State Bandit

    Event OnBeginState()
        If targ
            If _SLPQuest.SKSEInstalled
                UndressForBed()
            EndIf

            Snore = _SLPQuest._SLPSnoreLP.Play(targ)
            Sound.SetInstanceVolume(Snore, _SLPQuest.SnoreVol)
        EndIf
    EndEvent

    Event OnCellDetach()
        OnUnload()
    EndEvent

    Event OnUnload()
        If targ
            NoMoreNaked()
        EndIf
        Sound.StopInstance(Snore)
    EndEvent

    Event OnDetachedFromCell()
        OnUnload()
    EndEvent

    Event OnEffectFinish(Actor akTarget, Actor akCaster)
        OnUnload()
    EndEvent

    Event OnDying(Actor akKiller)
        GoToState("Dead")
    EndEvent

EndState

State Robe

    Event OnBeginState()
        If targ
            If _SLPQuest.SKSEInstalled
                UndressForBed()
                Chest = targ.GetWornForm(0x00000004)
                if Chest
                    targ.UnequipItem(Chest)
                endIf

                iRandomIndex = Utility.RandomInt(1, _SLPQuest._SLPRobesList.GetSize()) - 1
                LeveledItem slpLL = _SLPQuest._SLPRobesList.GetAt(iRandomIndex) as LeveledItem
                
                If slpLL
                    Trace("Sachink - " + slpLL + " Sleepign LL fetched")
                    Int NumPart = slpLL.GetNumForms()
                    while NumPart
                        Numpart -= 1
                        Armor cloth = slpLL.GetNthForm(NumPart) as Armor
                        targ.EquipItem(cloth, true)
                        ;targ.AddItem(cloth)
                    EndWhile
                EndIf
            EndIf

            Snore = _SLPQuest._SLPSnoreLP.Play(targ)
            Sound.SetInstanceVolume(Snore, _SLPQuest.SnoreVol)
        EndIf
    EndEvent

    Event OnCellDetach()
        OnUnload()
    EndEvent

    Event OnUnload()
        If targ
            targ.UnequipAll()
            NoMoreNaked()
        EndIf
        Sound.StopInstance(Snore)
    EndEvent

    Event OnDetachedFromCell()
        OnUnload()
    EndEvent

    Event OnEffectFinish(Actor akTarget, Actor akCaster)
        OnUnload()
    EndEvent

EndState

State Snore

    Event OnBeginState()
        If targ
            Snore = _SLPQuest._SLPSnoreLP.Play(targ)
            Sound.SetInstanceVolume(Snore, _SLPQuest.SnoreVol)
        EndIf
    EndEvent

    Event OnCellDetach()
        OnUnload()
    EndEvent

    Event OnUnload()
        Sound.StopInstance(Snore)
    EndEvent

    Event OnDetachedFromCell()
        OnUnload()
    EndEvent

    Event OnEffectFinish(Actor akTarget, Actor akCaster)
        OnUnload()
    EndEvent

EndState

State Dead
EndState

function UndressForBed()

    Head = targ.GetWornForm(0x00000002)
    if Head
        targ.UnequipItem(Head)
    endIf

    Boots = targ.GetWornForm(0x00000080)
    if Boots
        targ.UnequipItem(Boots)
    endIf

    Hands = targ.GetWornForm(0x00000008)
    if Hands
        targ.UnequipItem(Hands)
    endIf

    Cloak = targ.GetWornForm(0x00010000)
    if Cloak
        targ.UnequipItem(Cloak)
    endIf

    Backpack = targ.GetWornForm(0x00020000)
    if Backpack
        targ.UnequipItem(Backpack)
    endIf

    slot44 = targ.GetWornForm(0x00004000)
    if slot44
        targ.UnequipItem(slot44)
    endIf

    slot45 = targ.GetWornForm(0x00008000)
    if slot45
        targ.UnequipItem(slot45)
    endIf

    slot48 = targ.GetWornForm(0x00040000)
    if slot48
        targ.UnequipItem(slot48)
    endIf

    slot49 = targ.GetWornForm(0x00080000)
    if slot49
        targ.UnequipItem(slot49)
    endIf

    slot52 = targ.GetWornForm(0x00400000)
    if slot52
        targ.UnequipItem(slot52)
    endIf

    slot53 = targ.GetWornForm(0x00800000)
    if slot44
        targ.UnequipItem(slot53)
    endIf

    slot54 = targ.GetWornForm(0x01000000)
    if slot54
        targ.UnequipItem(slot54)
    endIf

    slot55 = targ.GetWornForm(0x02000000)
    if slot55
        targ.UnequipItem(slot55)
    endIf

    slot56 = targ.GetWornForm(0x04000000)
    if slot56
        targ.UnequipItem(slot56)
    endIf

    slot57 = targ.GetWornForm(0x08000000)
    if slot57
        targ.UnequipItem(slot57)
    endIf

    slot58 = targ.GetWornForm(0x10000000)
    if slot58
        targ.UnequipItem(slot58)
    endIf

    slot59 = targ.GetWornForm(0x20000000)
    if slot59
        targ.UnequipItem(slot59)
    endIf

    slot60 = targ.GetWornForm(0x40000000)
    if slot60
        targ.UnequipItem(slot60)
    endIf

    WeaponObj = targ.GetEquippedWeapon() as form
    if WeaponObj
        targ.UnequipItem(WeaponObj, true)
    endIf

    ShieldObj = targ.GetEquippedShield() as form
    if ShieldObj
        targ.UnequipItem(ShieldObj)
    endIf
endFunction

function NoMoreNaked()

    if head
        targ.EquipItem(Head)
    endIf
    if Chest
        targ.EquipItem(Chest)
    endIf
    if Boots
        targ.EquipItem(Boots)
    endIf
    if Hands
        targ.EquipItem(Hands)
    endIf
    if Cloak
        targ.EquipItem(Cloak)
    endIf
    if Backpack
        targ.EquipItem(Backpack)
    endIf
    if WeaponObj
        targ.EquipItem(WeaponObj)
    endIf
    if ShieldObj
        targ.EquipItem(ShieldObj)
    endIf
    if slot44
        targ.EquipItem(slot44)
    endIf
    if slot45
        targ.EquipItem(slot45)
    endIf
    if slot48
        targ.EquipItem(slot48)
    endIf
    if slot49
        targ.EquipItem(slot49)
    endIf
    if slot52
        targ.EquipItem(slot52)
    endIf
    if slot53
        targ.EquipItem(slot53)
    endIf
    if slot54
        targ.EquipItem(slot54)
    endIf
    if slot55
        targ.EquipItem(slot55)
    endIf
    if slot56
        targ.EquipItem(slot56)
    endIf
    if slot57
        targ.EquipItem(slot57)
    endIf
    if slot58
        targ.EquipItem(slot58)
    endIf
    if slot59
        targ.EquipItem(slot59)
    endIf
    if slot60
        targ.EquipItem(slot60)
    endIf

endFunction
