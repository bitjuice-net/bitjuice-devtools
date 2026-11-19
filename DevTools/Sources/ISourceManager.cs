namespace DevTools.Sources;

public interface ISourceManager
{
    void Add(string repository);
    void Remove(string repository);
    void List(string repository);
    void Sync();

    // Brings .app of an application to the revision in the synced index, before it is installed or updated.
    // Returns true when the file changed.
    bool Refresh(string application);

    // Replaces .app of an application with the one from its source, local changes included.
    void Reset(string application);
}
