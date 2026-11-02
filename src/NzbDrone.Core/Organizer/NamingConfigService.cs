namespace NzbDrone.Core.Organizer
{
    public interface INamingConfigService
    {
        NamingConfig GetConfig();
        void Save(NamingConfig namingConfig);
    }

    public class NamingConfigService : INamingConfigService
    {
        private readonly INamingConfigRepository _repository;
        private NamingConfig _cachedConfig;

        public NamingConfigService(INamingConfigRepository repository)
        {
            _repository = repository;
        }

        public NamingConfig GetConfig()
        {
            if (_cachedConfig != null)
            {
                return _cachedConfig;
            }

            var config = _repository.SingleOrDefault();

            if (config == null)
            {
                lock (_repository)
                {
                    config = _repository.SingleOrDefault();

                    if (config == null)
                    {
                        _repository.Insert(NamingConfig.Default);
                        config = _repository.Single();
                    }
                }
            }

            _cachedConfig = config;
            return config;
        }

        public void Save(NamingConfig namingConfig)
        {
            _repository.Upsert(namingConfig);
            _cachedConfig = namingConfig;
        }
    }
}
