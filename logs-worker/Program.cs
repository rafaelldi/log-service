using System.Reflection;
using logs_worker;
using logs_worker.HttpClients;
using static logs_worker.PolicyExtensions;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddHttpClient<SeqClient>(client =>
        {
            client.BaseAddress = new Uri("http://seq:5341");
        })
            .AddPolicyHandler(GetRetryPolicy());

        services.AddSingleton<DateTimeProvider>();
        services.AddSingleton<SeqExporter>();

        var exporters = Assembly.GetExecutingAssembly()
            .DefinedTypes
            .Where(x => x.GetInterfaces().Any(i => i == typeof(IFileParser)));
        foreach (var exporter in exporters)
        {
            services.Add(new ServiceDescriptor(typeof(IFileParser), exporter, ServiceLifetime.Singleton));
        }

        services.AddHostedService<Worker>();
    })
    .Build();

await host.RunAsync();