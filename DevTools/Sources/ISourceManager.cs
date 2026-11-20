namespace DevTools.Sources;

public interface ISourceManager
{
    void Add(string repository);
    void Remove(string repository);
    void List(string repository);

    // Brings .app of an application to the one in its source, before it is installed or updated.
    // Returns true when the file changed. A source is needed only when more than one has the application.
    bool Refresh(string application, string source = null);

    // Replaces .app of an application with the one from its source, or the one given, local changes included.
    void Reset(string application, string source = null);
}
