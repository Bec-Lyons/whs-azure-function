using System;

namespace WHSAzureFunction.Models
{
    [Serializable]
    public class NotificationInfo
    {
        public NotificationInfo()
        {
            notification = "Unprocessed Image";
            hat = false;
            vest = false;
            auth = "0";
            image = "";
        }

        public string notification { get; set; }
        public string name { get; set; }
        public string auth { get; set; }
        public bool hat { get; set; }
        public bool vest { get; set; }

        public string image { get; set; }

        public bool IsSafe()
        {
            return hat && vest;
        }
    }
}