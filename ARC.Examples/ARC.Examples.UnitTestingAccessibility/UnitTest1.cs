using ARCAPI;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace ARC.Examples.UnitTestingAccessibility
{
    public class Tests
    {
        ARCClient _client = null;

        [SetUp]
        public void Setup()
        {
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("arc-account-code", "Your Account Code");
            httpClient.DefaultRequestHeaders.Add("arc-subscription-key", "Your Subscription Code");

            _client = new ARCAPI.ARCClient("https://api.tpgarc.com/", httpClient);
        }

        [Test]
        public async Task TestAccessibility()
        {
            var domains = await _client.DomainsAsync();
            var policies = new List<TestInitiativePolicy>();

            //First get any initiative policies
            foreach (var domain in domains.Result)
            {
                var initiativesForDomain = await _client.InitiativesAsync(domain.Id);

                foreach (var policy in initiativesForDomain.Result)
                {
                    policies.Add(policy);
                }
            }

            var session = await _client.NewAsync();

            while (session.Result.Status != PooledMachineStatus._400)
            {
                await Task.Delay(1000);
                session = await _client.StatusAsync(session.Result.SessionId);
            }

            var sessionId = session.Result.SessionId;

            await _client.OpenAsync(sessionId);

            //for each policy set in the domains
            foreach (var policyGroup in policies.GroupBy(x => x.DomainID))
            {
                var domainConformance = await _client.AssetConformanceReport2Async(policyGroup.Key.Value);

                Assert.Multiple(async () =>
                {
                    //Check conformance for each unique asset
                    foreach (var assetConformance in domainConformance.Result.GroupBy(x => x.DigitalAssetID))
                    {
                        //Get Asset Details
                        var asset = await _client.Assets2Async(assetConformance.Key.Value);

                        //Open the url
                        await _client.Run2Async(sessionId, new AnalysisScriptStep
                        {
                            Command = "open",
                            Target = asset.Result.Url
                        });

                        //Scan the current page
                        var evaluationResults = await _client.Page3Async(sessionId);


                        foreach (var policy in assetConformance)
                        {
                            policy.CountNonConforming = evaluationResults.Result.Report
                                .Assertions.Count(x => x.Assertion == policy.Assertion);

                            //The policy is conforrming if the count of non conforming evaluations is less than or equal to the policy target
                            var policyIsConforming = policy.CountNonConforming > 0 && policy.CountNonConforming <= policy.TargetNonConforming;

                            Assert.IsTrue(policyIsConforming, $"{asset.Result.Url} failed policy: { policy.Title }");
                        }
                    }
                });
            }

            await _client.CloseAsync(sessionId);
        }
    }
}