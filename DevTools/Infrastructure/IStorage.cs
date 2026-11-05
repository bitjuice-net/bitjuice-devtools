namespace DevTools.Infrastructure
{
    public interface IStorage
    {
        T Load<T>(string fileName) where T : new();
        void Save<T>(string fileName, T obj) where T : new();
    }
}