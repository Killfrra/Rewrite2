using System;
using System.Numerics;
using System.Collections.Generic;

namespace Engine
{
    class GameObject
    {
        private static readonly LinkedList<GameObject> _list = new();
        private LinkedListNode<GameObject>? Node = null;
        private static int lastID = 0;
        public int NetID { get; init; } = lastID++;
        public void Add()
        {
            Node = _list.AddLast(this);
            OnAdd();
        }
        internal static void Loop(float delta)
        {
            foreach(var obj in _list)
            {
                obj.OnUpdate(delta);
            }
            foreach(var obj in _list)
            {
                obj.OnLateUpdate(delta);
            }
            foreach(var obj in _list)
            {
                obj.OnSync();
            }
        }

        protected virtual void OnAdd(){}
        protected virtual void OnUpdate(float delta){}
        protected virtual void OnLateUpdate(float delta){}
        protected virtual void OnSync(){}
        protected virtual void OnRemove(){}

        public void Remove()
        {
            if(Node != null)
            {
                _list.Remove(Node);
                Node = null;
                OnRemove();
            }
        }

        internal static void Reset(int cid)
        {
            foreach(var obj in _list)
            {
                obj.OnDisconnect(cid);
            }
        }
        protected virtual void OnDisconnect(int cid){}
    }

    class AttackableUnit: VisibleObject {
        public SlotManagerFrontend Slots;
        public AttackableUnit(){
            Slots = new(this);
        }
    }

    class AIUnit: AttackableUnit {
        Scriptable AI;
    }

    class Champion: AIUnit {}

    class Scriptable {
        protected virtual void OnActivate(){}
        protected virtual void OnDeactivate(){}
        public virtual void OnUpdate(float diff){}
    }

    class Icon: Scriptable {
        public int Slot { get; private set; }
        public AttackableUnit? Owner { get; private set; }
        internal void Activate(int slot, AttackableUnit owner){
            Slot = slot;
            Owner = owner;
            OnActivate();
        }
        internal void Deactivate(){
            Slot = -1;
            OnDeactivate();
        }
        internal virtual bool Stack(Icon another){
            return true;
        }
    }

    class ExtendedIcon: Icon {
        public AIUnit Caster { get; init; }
        public Scriptable? Parent { get; init; }
        /*
        ExtendedIcon(ObjAIBase caster, Scriptable? parent = null){
            Caster = caster;
            Parent = parent;
        }
        */

        public int MinStacks { get; init; } = 1;
        public int MaxStacks { get; init; } = 1;
        public int Stacks { get; protected set; } = 1;
        public float Duration { get; protected set; }
        public float DurationStartTime { get; protected set; }
        public float RemainingDuration => Duration - (Game.Time - DurationStartTime);
        
        public void AddStacks(int delta){
            int prevValue = Stacks;
            int unclamped = prevValue + delta;
            Stacks = Math.Clamp(unclamped, MinStacks, MaxStacks);
            if(Stacks != prevValue){
                OnUpdateAmmo(unclamped);
            }
        }
        public void Renew(float duration){
            Duration = duration; // + RemainingDuration
            DurationStartTime = Game.Time;
            OnUpdateTimer();
        }
        public void AddStacksAndRenew(int stacks, float duration){
            AddStacks(stacks);
            Renew(duration);
        }

        //TODO:
        public bool ReplaceWith(Buff buff){
            return Owner!.Slots.Replace(this, buff);
        }
        public bool OverlapWith(Buff buff){
            return Owner!.Slots.Overlap(this, buff);
        }
        public void Remove(){
            Owner!.Slots.Remove(this);
        }

        protected virtual void OnUpdateAmmo(int unclamped){}
        protected virtual void OnUpdateTimer(){}

        protected void SetToolTipVar(int index, int value){
            
        }
    }

    class Button: ExtendedIcon {
        public float Cooldown;
        private float CooldownStartTime;
        public float RemainingCooldown {
            get { return Cooldown - (Game.Time - CooldownStartTime); }
            set { CooldownStartTime = Game.Time + (value - Cooldown); }
        }
        float CastRange;
        internal virtual void Cast(
            /*
            AttackableUnit target,
            Vector2 position,
            Vector2 endPosition,
            bool overrideCastPosition = false,
            Vector2 overrideCastPositionVar = default,
            int overrideForceLevel,
            bool overrideCooldownCheck = false,
            bool fireWithoutCasting,
            bool forceCastingOrChannelling = false,
            bool updateAutoAttackTimer
            */
        ){}
        // AdjustCastInfo + AdjustCooldown
        
    }

    enum BuffType { /*...*/ }
    class Buff: ExtendedIcon {
        public BuffType Type { get; init; }
        public bool IsHiddenOnClient { get; init; }
    }
    class Spell: Button {
        int IconIndex = 0;
        int Level = 0;
        int MaxLevel = 4;
    }
    class Item: Button {}

    class SlotManager {
        protected AttackableUnit _owner;
        protected Dictionary<int, List<Icon>> _slots = new();
        internal SlotManager(AttackableUnit owner){
            _owner = owner;
        }
        
        public bool Has(Type type){
            return Get(type) != null;
        }
        public Icon? Get(Type type){
            return GetAll(type).First();
        }
        public IEnumerable<Icon> GetAll(Type type){
            foreach(var list in _slots.Values){
                for(int i = list.Count; i >= 0; i--){
                    var icon = list[i];
                    if(icon.GetType() == type){
                        yield return icon;
                    }
                }
            }
        }

        protected bool Add(Icon icon, int from = 0, int to = int.MaxValue){
            Icon? existing = Get(icon.GetType());
            if(existing != null){
                return existing.Stack(icon);
            } else {
                return AddToNewSlot(icon, from, to);
            }
        }
        private bool AddToNewSlot(Icon icon, int from = 0, int to = int.MaxValue){
            int slot = GetFreeSlot(from, to);
            return slot != -1 && AddToSlot(icon, slot);
        }
        private int GetFreeSlot(int from = 0, int to = int.MaxValue){
            for(int i = from; i < to; i++){
                if(!IsSlotOccupied(i)){
                    return i;
                }
            }
            return -1;
        }
        private bool IsSlotOccupied(int slot){
            return _slots.TryGetValue(slot, out var list) && list.Count > 0;
        }
        protected bool AddToSlot(Icon icon, int slot){
            List<Icon> list;
            if(_slots.TryGetValue(slot, out list)){
                if(list.Count > 0 && list[0].GetType() != icon.GetType()){
                    return false;
                }
            } else {
                _slots[slot] = list = new();
            }
            if(icon.Slot >= 0){
                Remove(icon);
            }
            list.Add(icon);
            icon.Activate(slot, _owner);
            return true;
        }
        public void Remove(Icon icon){
            int slot = icon.Slot;
            var list = _slots[slot];
            if(slot >= 0){
                icon.Deactivate();
            }
            list.Remove(icon);
        }
        public bool Replace(Icon one, Icon another){
            //TODO: Optimize by merging with AddToSlot?
            if(AddToSlot(another, one.Slot)){
                Remove(one);
                return true;
            }
            return false;
        }
        public bool Overlap(Icon one, Icon another){
            return AddToSlot(another, one.Slot);
        }
    }

    class SlotManagerFrontend: SlotManager {
        internal SlotManagerFrontend(AttackableUnit owner): base(owner){}

        //TODO:
        const int EXTRA_SLOTS_BEGIN = 0;
        const int EXTRA_SLOTS_END = 0;
        const int SPELL_SLOTS_BEGIN = 0;
        const int SPELL_SLOTS_END = 0;
        const int INVENTORY_SLOTS_BEGIN = 0;
        const int INVENTORY_SLOTS_END = 0;
        const int BUFF_SLOTS_BEGIN = 0;

        public bool Add(Item item, int slot = -1){
            return slot < 0 ?
                Add(item, INVENTORY_SLOTS_BEGIN, INVENTORY_SLOTS_END)
            : (
                slot >= INVENTORY_SLOTS_BEGIN && slot < INVENTORY_SLOTS_END
                && AddToSlot(item, slot)
            );
        }
        public bool Add(Spell spell, int slot = -1, bool extra = false){
            return slot < 0 ? (
                extra ?
                    Add(spell, EXTRA_SLOTS_BEGIN, EXTRA_SLOTS_END)
                :
                    Add(spell, SPELL_SLOTS_BEGIN, SPELL_SLOTS_END)
            ) : (
                (
                    extra ?
                        (slot >= EXTRA_SLOTS_BEGIN && slot < EXTRA_SLOTS_END)
                    :
                        (slot >=  SPELL_SLOTS_BEGIN && slot < SPELL_SLOTS_END)
                ) && AddToSlot(spell, slot)
            );
        }
        public bool Add(Buff buff){
            return Add(buff, BUFF_SLOTS_BEGIN);
        }

        void Cast(int slot){
            var list = _slots[slot];
            if(list != null && list.Count > 0){
                (list[0] as Button)?.Cast();
            }
        }
    }
}