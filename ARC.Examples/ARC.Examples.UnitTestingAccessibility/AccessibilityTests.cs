using System;
using ARCAPI;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Web;


namespace ARC.Examples.UnitTestingAccessibility
{
    public class Tests
    {
        private HttpClient _httpClient;

        [SetUp]
        public void Setup()
        {
            _httpClient = new HttpClient() {BaseAddress = new Uri("https://api.tpgarc.com/")};

            _httpClient.DefaultRequestHeaders.Add("arc-account-code", "<your account code from ARC | Settings>");
            _httpClient.DefaultRequestHeaders.Add("arc-subscription-key", "<your subscription key from ARC | Settings | Teams ");
        }

        
        [Test]
        public async Task ValidateAccessibility()
        {
            // this is a list of domains to test
            var domainUrls = new[]
            {
                "http://demo.admin.tpgarc.com",
                "https://demo.tpgarc.com",
                "https://staging.tpgarc.com"
            };
            
            var domains = await _httpClient.GetFromJsonAsync<DomainListARCResponse>("/v1/Account/Domains");
            var policies = new List<TestInitiativePolicy>();

            // pull the domains that we want to scan 
            foreach (var domain in domains.Result.Where(d => domainUrls.Contains(d.Url)))
            {
                // now pull the initiatives and policies
                var initiativesForDomain = await _httpClient.GetFromJsonAsync<TestInitiativePolicyIEnumerableARCResponse>($"/v1/AccessibilityPolicy/Domain/{domain.Id}/Initiatives");
                foreach (var policy in initiativesForDomain.Result)
                {
                    policies.Add(policy);
                }
            }

            // if there are no policies, there's nothing more to do.  If you are expecting to always have a policy, you could Assert() a failure here
            if (policies.Count == 0) 
                return;
            
            var session = await _httpClient.GetFromJsonAsync<AutomationSessionARCResponse>("/v1/Automation/Session/New");
            // start an analytics session.  The session is not ready until PooledMachineStatus == 400 
            while (session.Result.Status != PooledMachineStatus._400)
            {
                await Task.Delay(1000);
                session = await _httpClient.GetFromJsonAsync<AutomationSessionARCResponse>($"/v1/Automation/Session/Status?sessionId={session.Result.SessionId}&");
            }

            var sessionId = session.Result.SessionId;

            // open the browser for this session
            await _httpClient.PostAsync($"/v1/Automation/Session/{sessionId}/Browser/Open", null);

            //for each policy set in the domains
            foreach (var policyGroup in policies.GroupBy(x => x.DomainID))
            {
                var domainConformance = await _httpClient.GetFromJsonAsync<PolicyAssetConformanceIEnumerableARCResponse>($"/v1/AccessibilityPolicy/Domain/{policyGroup.Key.Value}/Initiatives/AssetConformanceReport");
                Assert.Multiple(async () =>
                {
                    //Check conformance for each unique asset
                    foreach (var assetConformance in domainConformance.Result.GroupBy(x => x.DigitalAssetID))
                    {
                        //Get Asset Details
                        var asset = await _httpClient.GetFromJsonAsync<AssetARCResponse>($"/v1/Assets/{assetConformance.Key.Value}");

                        //Open the url
                        // here we are going to ask the server-side browser session to open our URL
                        await _httpClient.PostAsJsonAsync($"/v1/Automation/Session/{sessionId}/Script/Step/Run", new AnalysisScriptStep
                        {
                            Command = "open",
                            Target = asset.Result.Url
                        });

                        //Scan the current page
                        var evaluationResults = await _httpClient.GetFromJsonAsync<AssetAnalyticsARCResponse>($"/v1/Automation/Session/{sessionId}/Analyze/Page");

                        foreach (var policy in assetConformance)
                        {
                            policy.CountNonConforming = evaluationResults.Result.Report
                                .Assertions.Count(x => x.Assertion == policy.Assertion);

                            //The policy is conforrming if the count of non conforming evaluations is less than or equal to the policy target
                            var policyIsConforming = policy.CountNonConforming > 0 && policy.CountNonConforming <= policy.TargetNonConforming;

                            Assert.IsTrue(policyIsConforming, $"{asset.Result.Url} failed policy: { policy.DisplayTitle }");
                        }
                    }
                });
            }

            await _httpClient.PostAsync($"/v1/Automation/Session/{sessionId}/Browser/Close", null);
        }

    }
}