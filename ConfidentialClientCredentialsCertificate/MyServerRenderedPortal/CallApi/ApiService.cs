﻿using Microsoft.Azure.KeyVault;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace MyServerRenderedPortal
{
    public class ApiService
    {
        private readonly IHttpClientFactory _clientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ApiService> _logger;

        public ApiService(IHttpClientFactory clientFactory, 
            IConfiguration configuration,
            ILoggerFactory loggerFactory)
        {
            _clientFactory = clientFactory;
            _configuration = configuration;
            _logger = loggerFactory.CreateLogger<ApiService>();
        }

        public async Task<JArray> GetApiDataAsync()
        {
            try
            {
                // Use Key Vault to get certificate
                var azureServiceTokenProvider = new AzureServiceTokenProvider();
                // using managed identities
                var kv = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(azureServiceTokenProvider.KeyVaultTokenCallback));

                // Get the certificate from Key Vault
                var identifier = _configuration["CallApi:ClientCertificates:0:KeyVaultCertificateName"];
                var cert = await GetCertificateAsync(identifier, kv);

                var client = _clientFactory.CreateClient();

                var scope = _configuration["CallApi:ScopeForAccessToken"];
                var authority = $"{_configuration["CallApi:Instance"]}{_configuration["CallApi:TenantId"]}";

                // client credentials flows, get access token
                IConfidentialClientApplication app = ConfidentialClientApplicationBuilder.Create(_configuration["CallApi:ClientId"])
                        .WithAuthority(new Uri(authority))
                        .WithCertificate(cert)
                        .WithLogging(MyLoggingMethod, Microsoft.Identity.Client.LogLevel.Verbose,
                            enablePiiLogging: true, enableDefaultPlatformLogging: true)
                        .Build();

                var accessToken = await app.AcquireTokenForClient(new[] { scope }).ExecuteAsync();

                client.BaseAddress = new Uri(_configuration["CallApi:ApiBaseAddress"]);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.AccessToken);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
       
                // use access token and get payload
                var response = await client.GetAsync("weatherforecast");
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var data = JArray.Parse(responseContent);

                    return data;
                }

                throw new ApplicationException($"Status code: {response.StatusCode}, Error: {response.ReasonPhrase}");
            }
            catch (Exception e)
            {
                throw new ApplicationException($"Exception {e}");
            }
        }

        private async Task<X509Certificate2> GetCertificateAsync(string identitifier, KeyVaultClient keyVaultClient)
        {
            var vaultBaseUrl = _configuration["CallApi:ClientCertificates:0:KeyVaultUrl"];

            var certificateVersionBundle = await keyVaultClient.GetCertificateAsync(vaultBaseUrl, identitifier);
            var certificatePrivateKeySecretBundle = await keyVaultClient.GetSecretAsync(certificateVersionBundle.SecretIdentifier.Identifier);
            var privateKeyBytes = Convert.FromBase64String(certificatePrivateKeySecretBundle.Value);
            var certificateWithPrivateKey = new X509Certificate2(privateKeyBytes, (string)null, X509KeyStorageFlags.MachineKeySet);
            return certificateWithPrivateKey;
        }

        void MyLoggingMethod(Microsoft.Identity.Client.LogLevel level, string message, bool containsPii)
        {
            _logger.LogInformation($"MSAL {level} {containsPii} {message}");
        }
    }
}