using System;

namespace Engine
{
    public static class ListExtensions
    {
        public static void Fill<T>(this List<T?> list, T? value)
        {
            for(int i = 0; i < list.Count; i++)
            {
                list[i] = value;
            }
        }
        public static void ResizeAndSet<T>(this List<T?> list, int i, T? value, T? def = default)
        {
            while(list.Count <= i)
            {
                list.Add(def);
            }
            list[i] = value;
        }
        public static T? GetValueOrDefault<T>(this List<T/*?*/> list, int i, T? def = default)
        {
            if(i >= list.Count)
            {
                return def;
            }
            return list[i];
        }
    }
    public abstract class Var
    {
        public abstract void Reset();
    }
    public abstract class Var<T>: Var
    {
        protected T? _def; // default for `_lastSeenValue`s
        protected T? _init; // default for `_v`alues
        protected List<T?> _lastSeenValue = new();
        public Var(T? init = default, T? def = default)
        {
            _init = init;
            _def = def;
            Reset();
        }
        public override void Reset()
        {
            _lastSeenValue.Fill(_def);
        }
    }
    public class PlayerVar<T>: Var<T>
    {
        T? _v;
        public PlayerVar(T? init = default, T? def = default): base(init, def)
        {
            _v = init;
        }
        public bool IsChanged(int cid) =>
            !object.Equals(_v, _lastSeenValue.GetValueOrDefault(cid, _def));
        public void Set(T? v)
        {
            _v = v;
        }
        public T? Get(int cid)
        {
            _lastSeenValue.ResizeAndSet(cid, _v, _def);
            return _v;
        }
    }
    public class PerPlayerVar<T>: Var<T>
    {
        List<T?> _v = new();
        public PerPlayerVar(T? init = default, T? def = default): base(init, def){}
        public bool IsChanged(int cid) => !object.Equals(_v[cid], _lastSeenValue[cid]);
        public void Set(int cid, T? v)
        {
            _v.ResizeAndSet(cid, v, _init);
        }
        public T? Get(int cid)
        {
            var v = _v[cid];
            _lastSeenValue.ResizeAndSet(cid, v, _def);
            return v;
        }
    }
}