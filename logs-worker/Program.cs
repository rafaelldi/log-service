using System.Reflection;
using Elastic.Clients.Elasticsearch;
using logs_worker;
using logs_worker.HttpClients;
using static logs_worker.PolicyExtensions;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        var elasticsearchClientSettings = new ElasticsearchClientSettings(new Uri("http://elasticsearch:9200"));
        var elasticsearchClient = new ElasticsearchClient(elasticsearchClientSettings);
        services.AddSingleton(elasticsearchClient);

        services.AddHttpClient<ElasticSearchClient>(client =>
        {
            client.BaseAddress = new Uri("http://elasticsearch:9200");
        })
            .AddPolicyHandler(GetRetryPolicy());

        services.AddHttpClient<KibanaClient>(client =>
        {
            client.BaseAddress = new Uri("http://kibana:5601");
            client.DefaultRequestHeaders.Add("kbn-xsrf", "true");
        })
            .AddPolicyHandler(GetRetryPolicy());

        var exporters = Assembly.GetExecutingAssembly()
            .DefinedTypes
            .Where(x => x.GetInterfaces().Any(i => i == typeof(IFileExporter)));
        foreach (var exporter in exporters)
        {
            services.Add(new ServiceDescriptor(typeof(IFileExporter), exporter, ServiceLifetime.Singleton));
        }

        services.AddHostedService<Worker>();
    })
    .Build();

host.Run();