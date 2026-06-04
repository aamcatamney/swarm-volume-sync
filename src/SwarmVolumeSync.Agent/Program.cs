using SwarmVolumeSync.Agent;
using SwarmVolumeSync.Core;

var config = AgentConfig.FromEnvironment();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.ListenAnyIP(config.ControlApiPort));

builder.Services.AddSingleton(config);
builder.Services.AddSingleton<IVolumeMetadataStore>(new FileMetadataStore(config.MetadataDirectory));
builder.Services.AddSingleton(new PeerDiscovery(config.TasksDnsName));
builder.Services.AddSingleton(new RsyncRunner());
builder.Services.AddHttpClient<PeerMetadataClient>();
builder.Services.AddSingleton(sp => new PeerMetadataClient(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(PeerMetadataClient)),
    config.ControlApiPort));
builder.Services.AddSingleton<MeshStatusService>();
builder.Services.AddSingleton<SourceRegistry>();
builder.Services.AddHostedService<SyncWorker>();

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.MapGet("/volumes", (IVolumeMetadataStore store) => Results.Ok(store.All()));

// Volumes this node currently sources, so peers can decide reclaim eligibility.
app.MapGet("/sources", (SourceRegistry sources) => Results.Ok(sources.Current));

// agentVersion is the software release (CONTEXT.md, Agent version), distinct
// from each volume's data generation under `volumes`.
app.MapGet("/status", async (MeshStatusService status, CancellationToken ct) =>
    Results.Ok(new
    {
        agentVersion = BuildInfoReader.Current.Version,
        commit = BuildInfoReader.Current.Commit,
        volumes = await status.BuildAsync(ct),
    }));

app.MapGet("/metrics", async (MeshStatusService status, CancellationToken ct) =>
    Results.Text(
        PrometheusFormatter.Format(await status.BuildAsync(ct), BuildInfoReader.Current),
        "text/plain; version=0.0.4"));

app.MapGet("/volumes/{name}/version", (string name, IVolumeMetadataStore store) =>
{
    var meta = store.TryGet(name);
    return meta is null
        ? Results.NotFound()
        : Results.Ok(new { generation = meta.Version.Generation });
});

app.MapGet("/volumes/{name}", (string name, IVolumeMetadataStore store) =>
{
    var meta = store.TryGet(name);
    return meta is null ? Results.NotFound() : Results.Ok(meta);
});

// Receives metadata propagated by the source after a successful data push.
app.MapPost("/volumes/{name}/metadata", (string name, VolumeMetadata metadata, IVolumeMetadataStore store) =>
{
    if (metadata.VolumeName != name)
        return Results.BadRequest("volume name mismatch");
    store.Save(metadata);
    return Results.Accepted();
});

app.Run();
