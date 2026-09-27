using System.Collections.ObjectModel;

namespace TCNet.Maui.Services;

public static class CollectionExtensions
{
    /// <summary>Makes <paramref name="target"/> equal to <paramref name="items"/>, replacing only the entries that changed, so views keep the rest.</summary>
    public static void Update<T>(this ObservableCollection<T> target, IReadOnlyList<T> items)
    {
        var eq = EqualityComparer<T>.Default;
        for (int i = 0; i < items.Count; i++)
        {
            if (i >= target.Count) target.Add(items[i]);
            else if (!eq.Equals(target[i], items[i])) target[i] = items[i];
        }
        while (target.Count > items.Count) target.RemoveAt(target.Count - 1);
    }
}
