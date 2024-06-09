using System.Collections;
using System.Runtime.InteropServices;
using Pack3r.Extensions;
using Pack3r.Models;

namespace Pack3r;

public sealed class ResourceList : ICollection<Resource>
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1067")]
    public readonly struct Key(Resource resource) : IEquatable<Key>
    {
        public ReadOnlyMemory<char> Value => resource.Value;
        public bool IsShader => resource.IsShader;
        public bool Equals(Key other) => IsShader.Equals(other.IsShader) && ROMCharComparer.Instance.Equals(Value, other.Value);
    }

    private readonly Dictionary<Key, Resource> _resources = [];

    public int Count => _resources.Count;
    public bool IsReadOnly => false;

    public bool Add(Resource item) => AddInternal(new Key(item), item);

    void ICollection<Resource>.Add(Resource item)
    {
        Key key = new(item);
        AddInternal(key, item);
    }

    public void AddRange(ResourceList other)
    {
        foreach (var (key, value) in other._resources)
        {
            AddInternal(key, value);
        }
    }

    private bool AddInternal(Key key, Resource item)
    {
        ref Resource? value = ref CollectionsMarshal.GetValueRefOrAddDefault(_resources, key, out _);

        if (value is null)
        {
            value = item;
            return true;
        }
        
        if (value.SourceOnly && !item.SourceOnly)
        {
            value = item;
            return true;
        }

        return false;
    }

    public void Clear() => _resources.Clear();
    public bool Contains(Resource item) => _resources.ContainsKey(new Key(item));
    public void CopyTo(Resource[] array, int arrayIndex) => _resources.Values.CopyTo(array, arrayIndex);
    public bool Remove(Resource item) => _resources.Remove(new Key(item));
    public Dictionary<Key, Resource>.ValueCollection.Enumerator GetEnumerator() => _resources.Values.GetEnumerator();
    IEnumerator<Resource> IEnumerable<Resource>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _resources.GetEnumerator();
}
