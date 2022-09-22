namespace Engine
{
    enum Category: int
    {
        Invisible = 0b00,
        FromInvisibleToVisible = 0b01,
        FromVisibleToInvisible = 0b10,
        Visible = 0b11,
        Count
    }

    class Var<T>
    {
        T? _v = default(T);
        T?[] _lastSeenState = new T[(int)Category.Count];
        public static implicit operator T?(Var<T> v)
        {
            return v._v;
        }
        public bool IsChanged(Category c)
        {
            T? last = _lastSeenState[(int)c];
            if(_v == null)
            {
                return last == null;
            }
            return _v.Equals(last);
        }
        public T? Get(Category c)
        {
            _lastSeenState[(int)c] = _v;
            return _v;
        }
        public void Set(T v)
        {
            _v = v;
        }
        public void Reset()
        {
            for(int c = 0; c < (int)Category.Count; c++)
            {
                _lastSeenState[(int)c] = default(T);
            }
        }
    }

    class VisibleObject: GameObject
    {
        int[] _clientsInCategory = new int[(int)Category.Count];
        Dictionary<int, Category> _clientInfos = new();
        protected override void OnSync()
        {
            for(int c = 0; c < (int)Category.Count; c++)
            {
                _clientsInCategory[(int)c] = 0;
            }
            for(int cid = 0; cid < Game.Avatars.Length; cid++)
            {
                bool seen = SeenByClient(cid);
                int c = (int)_clientInfos[cid];
                c = ((c & 1) << 1) | Convert.ToInt32(seen);
                _clientsInCategory[c]++;
                _clientInfos[cid] = (Category)c;
            }
            for(int c = 0; c < (int)Category.Count; c++)
            {
                OnSync((Category)c);
            }
        }
        protected virtual void OnSync(Category c){}
        protected virtual bool SeenByClient(int cid)
        {
            return true;
        }
        protected override void OnDisconnect(int cid)
        {
            _clientInfos[cid] = Category.Invisible;
        }
    }
}