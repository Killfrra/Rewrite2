using System;
using System.Numerics;
using System.Collections.Generic;
using LeaguePackets.Game;
using LeaguePackets;

namespace Engine
{
    public class GameObject
    {
        public Vector3 Position;

        private static readonly LinkedList<GameObject> _list = new();
        private LinkedListNode<GameObject>? Node = null;
        private static uint lastID = 0;
        public uint NetID { get; init; } = lastID++;
        public void Add()
        {
            Node = _list.AddLast(this);
            OnAdd();
        }
        internal static void Loop(float delta)
        {
            foreach (var obj in _list)
            {
                obj.OnUpdate(delta);
            }
            foreach (var obj in _list)
            {
                obj.OnLateUpdate(delta);
            }
            foreach (var obj in _list)
            {
                for (int cid = 0; cid < Game.Summoners.Length; cid++)
                {
                    obj.Sync(cid);
                }
            }
        }

        internal static void ReSync(int cid)
        {
            foreach (var obj in _list)
            {
                obj.Sync(cid);
            }
        }

        protected virtual void OnAdd() { }
        protected virtual void OnUpdate(float delta) { }
        protected virtual void OnLateUpdate(float delta) { }
        protected virtual void Sync(int cid) { }
        protected virtual void OnRemove() { }

        public void Remove()
        {
            if (Node != null)
            {
                _list.Remove(Node);
                Node = null;
                OnRemove();
            }
        }

        internal static void Disconnect(int cid)
        {
            foreach (var obj in _list)
            {
                obj.OnDisconnect(cid);
            }
        }
        protected virtual void OnDisconnect(int cid) { }

        internal static void Reconnect(int cid)
        {
            foreach (var obj in _list)
            {
                obj.OnReconnect(cid);
            }
        }
        protected virtual void OnReconnect(int cid) { }
    }

    public class HidingObject : GameObject
    {
        public Team Team;
        protected PlayerVar<bool> spawned = new(true);
        protected PerPlayerVar<bool> visible = new();
        protected override void Sync(int cid)
        {
            visible.Set(cid, SeenByClient(cid));
        }
        protected virtual bool SeenByClient(int cid)
        {
            return true;
        }
        protected override void OnDisconnect(int cid)
        {
            foreach (var propertyInfo in this.GetType().GetProperties())
            {
                if (propertyInfo.PropertyType.IsInstanceOfType(typeof(Var)))
                {
                    (propertyInfo.GetValue(this) as Var)?.Reset();
                }
            }
        }
    }

    public class AttackableUnit : HidingObject
    {
        public SlotManagerFrontend Slots;
        public string Name;
        public int Skin = 0;
        public AttackableUnit()
        {
            Slots = new(this);
        }
    }

    public class AIUnit : AttackableUnit
    {
        Scriptable AI;
    }

    public class Heal: Spell
    {
        public Heal()
        {
            NameOverride = "SummonerHeal";
            //HashOverride = 56930076;
        }
    }

    public class Flash: Spell
    {
        public Flash()
        {
            NameOverride = "SummonerFlash";
            //HashOverride = 105475752;
        }
    }

    public class Champion : AIUnit
    {
        public Summoner? Summoner;
        public Champion()
        {
            NetID = 0x40000001;
        }

        protected override void Sync(int cid)
        {
            if (spawned.IsChanged(cid))
            {
                spawned.Get(cid);
                var spawnHero = new S2C_CreateHero()
                {
                    Name = Summoner?.Name ?? "",
                    Skin = Name,
                    SkinID = Skin,
                    NetNodeID = 0x40,
                    NetID = NetID,
                    TeamIsOrder = Team == Team.Blue,
                    CreateHeroDeath = CreateHeroDeath.Alive,
                    ClientID = cid,
                    SpawnPositionIndex = 2,
                };
                Game.Server.SendEncrypted(cid, ChannelID.Broadcast, spawnHero);

                var avatarInfo = new AvatarInfo_Server();
                avatarInfo.SenderNetID = NetID;
                avatarInfo.SummonerIDs[0] = avatarInfo.SummonerIDs2[0] = Slots.GetSummonerSpell(0)?.GetHash() ?? 0;
                avatarInfo.SummonerIDs[1] = avatarInfo.SummonerIDs2[1] = Slots.GetSummonerSpell(1)?.GetHash() ?? 0;
                Game.Server.SendEncrypted(cid, ChannelID.Broadcast, avatarInfo);
            }
            else if (false)
            {

            }
            //base.Sync(cid);
        }
    }

    public class Scriptable
    {
        protected virtual void OnActivate() { }
        protected virtual void OnDeactivate() { }
        public virtual void OnUpdate(float diff) { }
    }

    public class Icon : Scriptable
    {
        public int Slot { get; private set; }
        public AttackableUnit? Owner { get; private set; }
        internal void Activate(int slot, AttackableUnit owner)
        {
            Slot = slot;
            Owner = owner;
            OnActivate();
        }
        internal void Deactivate()
        {
            Slot = -1;
            OnDeactivate();
        }
        internal virtual bool Stack(Icon another)
        {
            return true;
        }
    }

    public class ExtendedIcon : Icon
    {
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

        public void AddStacks(int delta)
        {
            int prevValue = Stacks;
            int unclamped = prevValue + delta;
            Stacks = Math.Clamp(unclamped, MinStacks, MaxStacks);
            if (Stacks != prevValue)
            {
                OnUpdateAmmo(unclamped);
            }
        }
        public void Renew(float duration)
        {
            Duration = duration; // + RemainingDuration
            DurationStartTime = Game.Time;
            OnUpdateTimer();
        }
        public void AddStacksAndRenew(int stacks, float duration)
        {
            AddStacks(stacks);
            Renew(duration);
        }

        //TODO:
        public bool ReplaceWith(Buff buff)
        {
            return Owner!.Slots.Replace(this, buff);
        }
        public bool OverlapWith(Buff buff)
        {
            return Owner!.Slots.Overlap(this, buff);
        }
        public void Remove()
        {
            Owner!.Slots.Remove(this);
        }

        protected virtual void OnUpdateAmmo(int unclamped) { }
        protected virtual void OnUpdateTimer() { }

        protected void SetToolTipVar(int index, int value)
        {

        }
    }

    public class Button : ExtendedIcon
    {
        public float Cooldown;
        private float CooldownStartTime;
        public float RemainingCooldown
        {
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
        )
        { }
        // AdjustCastInfo + AdjustCooldown

    }

    public enum BuffType { /*...*/ }
    public class Buff : ExtendedIcon
    {
        public BuffType Type { get; init; }
        public bool IsHiddenOnClient { get; init; }
    }

    class Utils
    {
        public static uint HashString(string path)
        {
            uint hash = 0;
            uint mask = 0xF0000000;
            for (var i = 0; i < path.Length; i++)
            {
                hash = char.ToLower(path[i]) + (hash << 4);
                uint t = hash & mask;
                hash ^= t ^ (t >> 24);
            }
            return hash;
        }
    }

    public class Spell : Button
    {
        public int IconIndex = 0;
        public int Level = 0;
        public int MaxLevel = 0;
        protected string? NameOverride;
        protected uint HashOverride = 0;
        public uint GetHash()
        {
            if(HashOverride != 0)
            {
                return HashOverride;
            }
            string? name = NameOverride;
            if(name == null)
            {
                name = this.GetType().Name;
            }
            HashOverride = Utils.HashString(name);
            return HashOverride;
        }
    }
    public class Item : Button { }

    public class SlotManager
    {
        protected AttackableUnit _owner;
        protected Dictionary<int, List<Icon>> _slots = new();
        internal SlotManager(AttackableUnit owner)
        {
            _owner = owner;
        }

        public bool Has<T>() where T : Icon
        {
            return Get<T> != null;
        }
        public T? Get<T>() where T : Icon
        {
            return GetAll<T>().First();
        }
        public IEnumerable<T> GetAll<T>() where T: Icon
        {
            foreach (var list in _slots.Values)
            {
                for (int i = list.Count; i >= 0; i--)
                {
                    if (list[i] is T icon)
                    {
                        yield return icon;
                    }
                }
            }
        }

        protected bool Add<T>(T icon, int from = 0, int to = int.MaxValue) where T : Icon
        {
            var existing = Get<T>();
            if (existing != null)
            {
                return existing.Stack(icon);
            }
            else
            {
                return AddToNewSlot(icon, from, to);
            }
        }
        private bool AddToNewSlot(Icon icon, int from = 0, int to = int.MaxValue)
        {
            int slot = GetFreeSlot(from, to);
            return slot != -1 && AddToSlot(icon, slot);
        }
        private int GetFreeSlot(int from = 0, int to = int.MaxValue)
        {
            for (int i = from; i < to; i++)
            {
                if (!IsSlotOccupied(i))
                {
                    return i;
                }
            }
            return -1;
        }
        private bool IsSlotOccupied(int slot)
        {
            return _slots.TryGetValue(slot, out var list) && list.Count > 0;
        }
        //TODO: Rename to MoveToSlot?
        protected bool AddToSlot(Icon icon, int slot)
        {
            List<Icon>? list;
            if (_slots.TryGetValue(slot, out list))
            {
                if (list.Count > 0 && list[0].GetType() != icon.GetType())
                {
                    return false;
                }
            }
            else
            {
                _slots[slot] = list = new();
            }
            if (icon.Slot >= 0)
            {
                Remove(icon);
            }
            list.Add(icon);
            icon.Activate(slot, _owner);
            return true;
        }
        public void Remove(Icon icon)
        {
            int slot = icon.Slot;
            var list = _slots[slot];
            if (slot >= 0)
            {
                icon.Deactivate();
            }
            list.Remove(icon);
        }
        public bool Replace(Icon one, Icon another)
        {
            //TODO: Optimize by merging with AddToSlot?
            if (AddToSlot(another, one.Slot))
            {
                Remove(one);
                return true;
            }
            return false;
        }
        public bool Overlap(Icon one, Icon another)
        {
            return AddToSlot(another, one.Slot);
        }
    }

    static class EnumExtensions
    {
        public static T Next<T>(this T src) where T : struct
        {
            T[] Arr = (T[])Enum.GetValues(src.GetType());
            int j = Array.IndexOf<T>(Arr, src) + 1;
            return Arr[j];
        }
    }

    public class SlotManagerFrontend : SlotManager
    {
        private enum SlotType
        {
            Spell = 0,
            SummonerSpell = 4,
            Item = 6,
            BluePill = 13,
            TempItem = 14,
            Rune = 15,
            Extra = 45,
            Buff = 0, //TODO:
        }
        internal SlotManagerFrontend(AttackableUnit owner) : base(owner) { }

        private int GetSlot(SlotType type, int offset = 0)
        {
            int slot = (int)type + offset;
            if (slot >= (int)type && slot < (int)type.Next())
            {
                return slot;
            }
            throw new ArgumentOutOfRangeException();
        }
        private Icon? Get(SlotType type, int offset = 0)
        {
            int slot = GetSlot(type, offset);
            return _slots.GetValueOrDefault(slot)?.GetValueOrDefault(0);
        }
        private void Set(SlotType type, int offset, Icon? icon)
        {
            int slot = GetSlot(type, offset);
            List<Icon>? list;
            if (_slots.TryGetValue(slot, out list))
            {
                //TODO: failsafe
                foreach(var existing in list)
                {
                    existing.Deactivate();
                }
                list.Clear();
            }
            else
            {
                _slots[slot] = list = new();
            }
            if(icon != null)
            {
                list.Add(icon);
                icon.Activate(slot, _owner);
            }
        }

        public bool Add(Item item)
        {
            return Add(item, (int)SlotType.Item, (int)SlotType.Item.Next());
        }
        public void Set(int slot, Spell spell)
        {
            Set(SlotType.Spell, slot, spell);
        }
        public void SetExtra(int slot, Spell spell)
        {
            Set(SlotType.Extra, slot, spell);
        }
        public Spell? GetSummonerSpell(int n)
        {
            return Get(SlotType.SummonerSpell, n) as Spell;
        }
        public void SetSummonerSpell(int slot, Spell spell)
        {
            Set(SlotType.SummonerSpell, slot, spell);
        }
        public bool Add(Buff buff)
        {
            return Add(buff, (int)SlotType.Buff);
        }

        void Cast(int slot)
        {
            var list = _slots[slot];
            if (list != null && list.Count > 0)
            {
                (list[0] as Button)?.Cast();
            }
        }
    }
}