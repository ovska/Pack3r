﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Pack3r;

public class ResourceCoordinator(
    ILogger<ResourceCoordinator> logger,
    IOptions<PackOptions> options,
    IShaderParser shaderParser,
    IEnumerable<IResourceParser> resourceParsers)
{
    private readonly PackOptions _options = options.Value;

    public async Task Test(Map map, CancellationToken cancellationToken)
    {
        List<Task> tasks = [];

        var readShadersTask = shaderParser.ParseAllShaders(map.ETMain, null, cancellationToken);

        ConcurrentDictionary<Resource, object?> resources = [];

        await Parallel.ForEachAsync(resourceParsers, cancellationToken, async (parser, ct) =>
        {
            string path = parser.GetPath(map);

            if (!File.Exists(path))
            {
                logger.LogInformation("File '{path}' not found, skipping...", path);
                return;
            }

            await foreach (var resource in parser.Parse(path, ct).ConfigureAwait(false))
            {
                resources.TryAdd(resource, null);
            }
        }).ConfigureAwait(false);


    }
}
