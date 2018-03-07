using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;


using System.IO;
using Microsoft.WindowsAzure.Storage;
using System.Configuration;
using Microsoft.Azure;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.ProjectOxford.Face;
using Microsoft.ProjectOxford.Vision;

using System;
using System.Text;
using Newtonsoft.Json;

namespace WHSAzureFunction
{
    public static class ImageAnalyser
    {

        private static IFaceServiceClient faceServiceClient = new FaceServiceClient("bd8f168ffc814ec7a3d4d7e008455010", "https://australiaeast.api.cognitive.microsoft.com/face/v1.0");
        private static IVisionServiceClient _visionClient = new VisionServiceClient("7d545178d66d4b189f4e26233268c7e7", "https://australiaeast.api.cognitive.microsoft.com/vision/v1.0");

        [FunctionName("ImageAnalyser")]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {
            log.Info("C# HTTP trigger function processed a request.");

            // Get request body
            byte[] image = await req.Content.ReadAsByteArrayAsync();

            if (image != null)
            {
                try
                {
                    await ProcessImage(image, log);
                }
                catch (ArgumentException e)
                {
                    await SendEventsToTopic(new Event(), log);
                    return req.CreateResponse(HttpStatusCode.OK, "No Safety Gear found!");
                }
                catch (InvalidOperationException e)
                {
                    await SendEventsToTopic(new Event(), log);
                    return req.CreateResponse(HttpStatusCode.OK, "Unrecognised person!");
                }
                catch (Microsoft.ProjectOxford.Face.FaceAPIException e)
                {
                    await SendEventsToTopic(new Event(), log);
                    return req.CreateResponse(HttpStatusCode.OK, "FACE API FAIL");
                }

            }

            return req.CreateResponse(HttpStatusCode.OK, "Hello");
        }


        public static async Task ProcessImage(byte[] image, TraceWriter log)
        {
            await Task.WhenAll(SaveToBlob(image, log), IdentifyFace(image, log));
        }

        public static async Task SaveToBlob(byte[] image, TraceWriter log)
        {
            //string accessKey = ConfigurationManager.AppSettings["StorageAccessKey"];
            //string accountName = ConfigurationManager.AppSettings["StorageAccountName"];
            string accessKey = "";
            string accountName = "";
            string connectionString = "DefaultEndpointsProtocol=https;AccountName=" + accountName + ";AccountKey=" + accessKey + ";EndpointSuffix=core.windows.net";
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);

            CloudBlobClient client;
            CloudBlobContainer container;

            client = storageAccount.CreateCloudBlobClient();
            container = client.GetContainerReference("archiveimages");
            await container.CreateIfNotExistsAsync();

            CloudBlockBlob blob;
            string name;

            //change to time string
            name = Guid.NewGuid().ToString("n") + ".jpg";

            blob = container.GetBlockBlobReference(name);

            using (Stream stream = new MemoryStream(image))
            {
                await blob.UploadFromStreamAsync(stream);
            }
            log.Info("BLOB SAVED");

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
                        //throw new System.InvalidOperationException("Logfile cannot be read-only");    
                        //break
                    }
                    else
                    {
                        log.Info("COULD NOT FIND FACE");
                        //break
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
                    throw new System.InvalidOperationException("Logfile cannot be read-only");
                    //SEND EVENT
                    //break;
                }
                else
                {
                    // Get top 1 among all candidates returned
                    var candidateId = identifyResult.Candidates[0].PersonId;
                    var person = await faceServiceClient.GetPersonAsync("whsworkers", candidateId);
                    log.Info("FACE IS " + person.Name);

                    //CHECK AUTHORISED
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
                }

                if (tags.Any(t => t.Name == "orange") || tags.Any(t => t.Name == "yellow"))
                {
                    log.Info("Safety Vest = CHECK");
                }

                else
                {
                    log.Info("SAFETY GEAR UNDETECTED!");
                    throw new System.ArgumentException("Parameter cannot be null", "original");
                    //SEND EVENT
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
            httpClient.DefaultRequestHeaders.Add("aeg-sas-key", "o5R2fcvCqw+Sd1B8Ex8q3/isGY1Pil9hGab+91YTRA8=");

            // Event grid expects event data as JSON
            var json = JsonConvert.SerializeObject(new Event[] { events });

            // Create request which will be sent to the topic
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Send request
            var result = await httpClient.PostAsync("https://unsafenotifications.southeastasia-1.eventgrid.azure.net/api/events", content);
            log.Info("EVENT GRID LOGGED" + result);
        }
    }


    /// <summary>
    /// Event to be sent to Event Grid Topic.
    /// </summary>
    public class Event
    {

        /// <summary>
        /// Gets the unique identifier for the event.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Gets the publisher defined path to the event subject.
        /// </summary>
        public string Subject { get; set; }

        /// <summary>
        /// Gets the registered event type for this event source.
        /// </summary>
        public string EventType { get; }

        /// <summary>
        /// Gets the time the event is generated based on the provider's UTC time.
        /// </summary>
        public string EventTime { get; }

        /// <summary>
        /// Gets or sets the event data specific to the resource provider.
        /// </summary>
        public NotificationInfo Data { get; set; }

        /// <summary>
        /// Constructor.
        /// </summary>
        public Event()
        {
            Id = Guid.NewGuid().ToString();
            EventType = "UnsafeNotificationsSubscription";
            Subject = "UnsafeNotificationsSubscription";
            NotificationInfo data = new NotificationInfo();
            data.NotificationType = "Wrong Gear";
            Data = data;
            EventTime = DateTime.UtcNow.ToString("o");
        }
    }

    public class NotificationInfo
    {
        public string NotificationType { get; set; }
    }
}



    
