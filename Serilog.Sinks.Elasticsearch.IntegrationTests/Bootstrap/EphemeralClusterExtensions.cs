using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using Elastic.Managed.Ephemeral;
using Elastic.Xunit;
using Elasticsearch.Net;
using Nest;

namespace Serilog.Sinks.Elasticsearch.IntegrationTests.Bootstrap
{
    public static class EphemeralClusterExtensions
    {
        private static readonly bool RunningMitmProxy = Process.GetProcessesByName("mitmproxy").Any();
        private static readonly bool RunningFiddler = Process.GetProcessesByName("fiddler").Any();
        private static string LocalOrProxyHost => RunningFiddler || RunningMitmProxy ? "ipv4.fiddler" : "localhost";

        public static ConnectionSettings CreateConnectionSettings<TConfig>(this IEphemeralCluster<TConfig> cluster)
            where TConfig : EphemeralClusterConfiguration
        {
            var clusterNodes = cluster.NodesUris(LocalOrProxyHost);
            return new ConnectionSettings(new StaticConnectionPool(clusterNodes));
        }

        public static IElasticClient GetOrAddClient<TConfig>(
            this IEphemeralCluster<TConfig> cluster,
            Func<ConnectionSettings, ConnectionSettings> modifySettings = null
        )
            where TConfig : EphemeralClusterConfiguration
        {
            modifySettings = modifySettings ?? (s => s);
            return cluster.GetOrAddClient(c =>
            {
                var settings = modifySettings(cluster.CreateConnectionSettings());

                var current = (IConnectionConfigurationValues)settings;
                var notAlreadyAuthenticated = current.BasicAuthenticationCredentials == null && current.ClientCertificates == null;
                var noCertValidation = current.ServerCertificateValidationCallback == null;

                if (cluster.ClusterConfiguration.EnableSecurity && notAlreadyAuthenticated)
                    settings = settings.BasicAuthentication(ClusterAuthentication.Admin.Username, ClusterAuthentication.Admin.Password);
                if (cluster.ClusterConfiguration.EnableSsl && noCertValidation)
                {
                    //todo use CA callback instead of allow all
                    // ReSharper disable once UnusedVariable
                    var ca = new X509Certificate2(cluster.ClusterConfiguration.FileSystem.CaCertificate);
                    settings = settings.ServerCertificateValidationCallback(CertificateValidations.AllowAll);
                }
                var client = new ElasticClient(settings);
                return client;
            });
        }
    }
}