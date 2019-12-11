﻿using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Peppy.Core;

namespace Peppy.ServiceRegistry.Consul.Provider
{
    public class SimpleConsulConfigurationProvider : ConfigurationProvider
    {
        private readonly string _path; private readonly IReadOnlyList<Uri> _consulUrls; public SimpleConsulConfigurationProvider(IEnumerable<Uri> consulUrls, string path)
        {
            _path = path;
            _consulUrls = consulUrls.Select(u => new Uri(u, $"v1/kv/{path}")).ToList();
            if (_consulUrls.Count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(consulUrls));
            }
        }
        public override void Load() => LoadAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        private async Task LoadAsync()
        {
            Data = await ExecuteQueryAsync();
        }
        private async Task<IDictionary<string, string>> ExecuteQueryAsync()
        {
            int consulUrlIndex = 0;
            while (true)
            {
                try
                {
                    var requestUri = "?recurse=true";
                    using var httpClient = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip }, true);
                    using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_consulUrls[consulUrlIndex], requestUri));
                    using var response = await httpClient.SendAsync(request);
                    response.EnsureSuccessStatusCode();
                    var tokens = JsonHelper.FromJsonList<ConsulConfigurationEnitiy>(await response.Content.ReadAsStringAsync());
                    return tokens
                        .Select(k => new KeyValuePair<string, JToken>
                        (
                          k.Key.Substring(_path.Length),
                          k.Value != null ? JToken.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(k.Value))) : null
                        ))
                      .Where(v => !string.IsNullOrWhiteSpace(v.Key))
                      .SelectMany(Flatten)
                      .ToDictionary(v => ConfigurationPath.Combine(v.Key.Split('/')), v => v.Value, StringComparer.OrdinalIgnoreCase);
                }
                catch
                {
                    consulUrlIndex += 1; 
                    if (consulUrlIndex >= _consulUrls.Count) throw;
                }
            }
        }
        private static IEnumerable<KeyValuePair<string, string>> Flatten(KeyValuePair<string, JToken> tuple)
        {
            if (!(tuple.Value is JObject value))
                yield break;
            foreach (var property in value)
            {
                var propertyKey = $"{tuple.Key}/{property.Key}";
                switch (property.Value.Type)
                {
                    case JTokenType.Object:
                        foreach (var item in Flatten(new KeyValuePair<string, JToken>(propertyKey, property.Value)))
                            yield
                                return item;
                        break;
                    case JTokenType.Array:
                        break;
                    default:
                        yield
                       return new KeyValuePair<string, string>(propertyKey, property.Value.Value<string>());
                        break;
                }
            }
        }
    }
}
