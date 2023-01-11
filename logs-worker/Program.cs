using Elastic.Clients.Elasticsearch;
using logs_worker;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        var elasticsearchClientSettings = new ElasticsearchClientSettings(new Uri("http://elasticsearch:9200"));
        var elasticsearchClient = new ElasticsearchClient(elasticsearchClientSettings);
        services.AddSingleton(elasticsearchClient);

        services.AddHttpClient("elasticsearch", client =>
        {
            client.BaseAddress = new Uri("http://elasticsearch:9200");
        });
        
        services.AddHttpClient("kibana", client =>
        {
            client.BaseAddress = new Uri("http://kibana:5601");
            client.DefaultRequestHeaders.Add("kbn-xsrf", "true");
        });
        
        services.AddHostedService<Worker>();
    })
    .Build();

host.Run();
