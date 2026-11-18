using DevTools.Sources.Models;

namespace DevTools.Sources;

public interface ISourceManager
{
    void Add(string repository);
    void Remove(string repository);
    void List(string repository);
    void Sync();

    // Where the manifests of an application came from, null when they were not fetched from a source.
    FetchedApp GetOrigin(string application);

    // Brings .app and .source of an application to the revision in the synced index, before it is installed or updated.
    // Returns true when a file changed.
    bool Refresh(string application);
}
