using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.ProjectOxford.Face;
using Microsoft.ProjectOxford.Vision;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using WHSAzureFunction.Exceptions;
using WHSAzureFunction.Models;

namespace WHSAzureFunction
{
    public static class ImageAnalyser
    {
        //TODO: take out keys
        private static IFaceServiceClient faceServiceClient;
        private static IVisionServiceClient _visionClient;
        private static NotificationInfo data;

        [FunctionName("ImageAnalyser")]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {
            // Get request body
            byte[] image = await req.Content.ReadAsByteArrayAsync();


            if (image != null)
            {
                faceServiceClient = new FaceServiceClient(GetEnvironmentVariable("FaceApiKey"), GetEnvironmentVariable("FaceApiEndpoint"));
                _visionClient = new VisionServiceClient(GetEnvironmentVariable("ComputerVisionApiKey"), GetEnvironmentVariable("ComputerVisionApiEndpoint"));
                data = new NotificationInfo();
                
                try
                {
                    log.Info("Processing Image...");
                    await ProcessImage(image, log);
                }
                catch (NoGearException)
                {
                    //Send no gear event
                    data.notification = "No Safety Gear Detected";
                    await SendEventsToTopic(new Event(data, "NoGearEvent"), log);
                    return GenerateResponse(req);
                }
                catch (UnrecognisedFaceException)
                {
                    data.notification = "Unrecognised Person";
                    //Send notification that unrecognised person is attempting to enter site
                    await SendEventsToTopic(new Event(data, "UnrecognisedFaceEvent"), log);
                    return GenerateResponse(req);
                }
                catch (FaceAPIException)
                {
                    data.notification = "Unprocessed Image";
                    return GenerateResponse(req);
                }
                catch (TooManyFacesException)
                {
                    data.notification = "Too many faces";
                    return GenerateResponse(req);
                }
                catch (NoFaceException)
                {
                    data.notification = "No Face Detected";
                    return GenerateResponse(req);
                }
            }

            return req.CreateResponse(HttpStatusCode.BadRequest, "Image not recognised");
        }

        public static async Task ProcessImage(byte[] image, TraceWriter log)
        {
            //option to save image to blob
            await Task.WhenAll(//SaveToBlob(image, log),
                IdentifyFace(image, log));
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
            container = client.GetContainerReference("unrecognisedface" + DateTime.Now.ToString("ddhmm"));

            if (!await container.ExistsAsync())
            {
                await container.CreateIfNotExistsAsync();
                log.Info("Container created at: " + DateTime.Now.ToString("h-mm-ss tt"));
            }

            data.image = container.StorageUri.PrimaryUri + "";
            CloudBlockBlob blob;
            string name;

            name = Guid.NewGuid().ToString("n") + ".png";

            blob = container.GetBlockBlobReference(name);

            using (Stream stream = new MemoryStream(image))
            {
                await blob.UploadFromStreamAsync(stream);
            }

            log.Info("Image saved into Blob Storage");
        }

        public static async Task IdentifyFace(byte[] image, TraceWriter log)
        {
            try
            {
                using (Stream stream = new MemoryStream(image))
                {
                    var faces = await faceServiceClient.DetectAsync(stream);

                    if (faces.Length == 1)
                    {
                        log.Info("ONE FACE FOUND");

                        //Identify faces + Check Gear
                        await Task.WhenAll(CheckAuthorized(faces, log), IdentifyGear(image, log));
                    }
                    else if (faces.Length > 1)
                    {
                        log.Info("TOO MANY FACES");
                        throw new TooManyFacesException();
                    }
                    else
                    {
                        log.Info("COULD NOT FIND FACE");
                        throw new NoFaceException();
                    }
                }
            }
            catch (FaceAPIException e)
            {
                log.Info(e.ToString());
            }
        }

        public static async Task CheckAuthorized(Microsoft.ProjectOxford.Face.Contract.Face[] faces, TraceWriter log)
        {
            var faceIds = faces.Select(face => face.FaceId).ToArray();
            var results = await faceServiceClient.IdentifyAsync("whsworkers", faceIds);
            foreach (var identifyResult in results)
            {
                if (identifyResult.Candidates.Length == 0)
                {
                    log.Info("FACE UNRECOGNISED");
                    data.notification = "Unrecognised Worker";
                    throw new UnrecognisedFaceException();
                }
                else
                {
                    // Get top 1 among all candidates returned
                    var candidateId = identifyResult.Candidates[0].PersonId;
                    var person = await faceServiceClient.GetPersonAsync("whsworkers", candidateId);
                    data.name = person.Name;
                    log.Info("FACE IS " + person.Name);
                }
            }
        }

        public static async Task IdentifyGear(byte[] image, TraceWriter log)
        {
            using (Stream stream = new MemoryStream(image))
            {
                var analysisResult = await _visionClient.GetTagsAsync(stream);
                var tags = analysisResult.Tags.ToList();
                if (tags.Any(t => t.Name == "hat") || tags.Any(t => t.Name == "helmet") ||
                           tags.Any(t => t.Name == "headdress"))
                {
                    log.Info("Safety Hat = CHECK");
                    data.hat = true;
                }

                if (tags.Any(t => t.Name == "orange") || tags.Any(t => t.Name == "yellow"))
                {
                    log.Info("Safety Vest = CHECK");
                    data.vest = true;
                }

                if (!data.IsSafe())
                {
                    throw new NoGearException();
                }
            }
        }

        /// <summary>
        /// Send events to Event Grid Topic.
        /// </summary>
        private static async Task SendEventsToTopic(Event events, TraceWriter log)
        {
            // Create a HTTP client which we will use to post to the Event Grid Topic
            var httpClient = new HttpClient();

            // Add key in the request headers
            httpClient.DefaultRequestHeaders.Add("aeg-sas-key", GetEnvironmentVariable("EventGridKey"));

            // Event grid expects event data as JSON
            var json = JsonConvert.SerializeObject(new Event[] { events });

            // Create request which will be sent to the topic
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Send request
            var result = await httpClient.PostAsync(GetEnvironmentVariable("EventGridEndpoint"), content);
            log.Info("Event sent to EventGrid");
        }

        private static HttpResponseMessage GenerateResponse(HttpRequestMessage req)
        {
            HttpResponseMessage response = req.CreateResponse(HttpStatusCode.OK, JsonConvert.SerializeObject(data));
            if (req.Headers.Contains("Origin"))
            {
                response.Headers.Add("Access-Control-Allow-Credentials", "true");
                response.Headers.Add("Access-Control-Allow-Origin", "*");
                response.Headers.Add("Access-Control-Allow-Methods", "POST, OPTIONS");
            }
            return response;
        }

        public static string GetEnvironmentVariable(string name)
        {
            return Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
        }
    }
}