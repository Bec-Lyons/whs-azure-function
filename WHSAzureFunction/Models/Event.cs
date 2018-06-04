using System;

namespace WHSAzureFunction.Models
{
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
        public Event(NotificationInfo data, string EventType)
        {
            Id = Guid.NewGuid().ToString();
            this.EventType = EventType;
            Subject = "NoGearEvent";
            this.Data = data;
            EventTime = DateTime.UtcNow.ToString("o");
        }

        public Event(string EventType)
        {
            this.EventType = EventType;
        }
    }
}