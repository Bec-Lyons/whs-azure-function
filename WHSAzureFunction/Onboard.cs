using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Mono.Web;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace WHSAzureFunction
{
    public static class Onboard
    {
        private static string BaseURI;

        [FunctionName("Onboard")]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {
            byte[] image = await req.Content.ReadAsByteArrayAsync();

            BaseURI = GetEnvironmentVariable("FaceApiEndpoint") + "/persongroups/" + GetEnvironmentVariable("FaceApiPersonGroup");

            string name = req.GetQueryNameValuePairs()
            .FirstOrDefault(q => string.Compare(q.Key, "name", true) == 0)
            .Value;

            if (image != null && name != null)
            {
                log.Info("Processing Image...");

                try
                {
                    //Create Face
                    string newpersonData = await CreatePerson(name, log);
                    CreateFaceData e = JsonConvert.DeserializeObject<CreateFaceData>(newpersonData);
                    string personID = e.personId;
                    log.Info(personID);

                    //add image against face
                    log.Info(await AddPersonFace(personID, image, log));

                    //train face API
                    await TrainAPIAsync();
                }
                catch (Exception e)
                {
                    return req.CreateResponse(HttpStatusCode.InternalServerError, "Problems with FaceAPI: " + e.Source +" "+ e.Message);
                }
                //await SaveToBlob(image, log);
            }
            // parse query parameter

            return name == null
                ? req.CreateResponse(HttpStatusCode.BadRequest, "Please pass a name on the query string or in the request body")
                : req.CreateResponse(HttpStatusCode.OK, "Successfully initiated " + name + " into the system.");
        }

        private static async Task<string> MakeRequestAsync(byte[] byteData, string method, string uri)
        {
            var client = new HttpClient();
            var queryString = HttpUtility.ParseQueryString(string.Empty);

            // Request headers
            client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", GetEnvironmentVariable("FaceApiKey"));

            HttpResponseMessage response;

            using (var content = new ByteArrayContent(byteData))
            {
                content.Headers.ContentType = new MediaTypeHeaderValue(method);
                response = await client.PostAsync(uri, content);
                return await response.Content.ReadAsStringAsync();
            }
        }

        private static async Task TrainAPIAsync()
        {
            await MakeRequestAsync(new byte[0], "application/json", BaseURI+ "/train");
        }

        private async static Task<string> CreatePerson(string name, TraceWriter log)
        {
            byte[] byteData = Encoding.UTF8.GetBytes("{\"name\":\"" + name + "\",\"userData\":\"" + name + "\"}");
            return await MakeRequestAsync(byteData, "application/json", BaseURI+"/persons");
        }

        private async static Task<string> AddPersonFace(string personID, byte[] byteData, TraceWriter log)
        {
            var uri = BaseURI +"/persons/" + personID + "/persistedFaces";
            return await MakeRequestAsync(byteData, "application/octet-stream", uri);
        }

        public static async Task SaveToBlob(byte[] image, TraceWriter log)
        {
            string accessKey = GetEnvironmentVariable("StorageAccessKey");
            string accountName = GetEnvironmentVariable("StorageAccountName");
            string connectionString = "DefaultEndpointsProtocol=https;AccountName=" + accountName + ";AccountKey=" + accessKey + ";EndpointSuffix=core.windows.net";

            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);

            CloudBlobClient client;
            CloudBlobContainer container;

            client = storageAccount.CreateCloudBlobClient();

            container = client.GetContainerReference("newperson");
            if (!await container.ExistsAsync())
            {
                await container.CreateIfNotExistsAsync();
            }

            //data.image = container.StorageUri.PrimaryUri + "";
            CloudBlockBlob blob;
            string name = "NewPerson.png";

            blob = container.GetBlockBlobReference(name);

            using (Stream stream = new MemoryStream(image))
            {
                await blob.UploadFromStreamAsync(stream);
            }
            log.Info("Image saved to blob.");
        }

        public static string GetEnvironmentVariable(string name)
        {
            return Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
        }
    }

    internal class CreateFaceData
    {
        /// <summary>
        /// Gets the unique identifier for the event.
        /// </summary>
        public string personId { get; set; }
    }


}