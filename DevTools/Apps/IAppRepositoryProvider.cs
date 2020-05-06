namespace DevTools.Apps
{
    public interface IAppRepositoryProvider
    {
        AppRepository Load();
        void Save(AppRepository repository);
    }
}