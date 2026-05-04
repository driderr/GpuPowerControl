using System;

namespace GpuThermalController.Core
{
    /// <summary>
    /// Type of event raised by the thermal controller.
    /// </summary>
    public enum ControllerEventType
    {
        Info,
        Warning,
        Trigger,
        Emergency,
        Stable
    }

    /// <summary>
    /// Event arguments raised when the controller state changes.
    /// </summary>
    public class ThermalControllerEventArgs : EventArgs
    {
        public uint Temperature { get; }
        public int PowerLimit { get; }
        public bool IsControlling { get; }
        public string? Message { get; }
        public ControllerEventType EventType { get; }

        public ThermalControllerEventArgs(uint temperature, int powerLimit, bool isControlling, ControllerEventType eventType, string? message = null)
        {
            Temperature = temperature;
            PowerLimit = powerLimit;
            IsControlling = isControlling;
            EventType = eventType;
            Message = message;
        }
    }
}