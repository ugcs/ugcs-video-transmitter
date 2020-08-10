﻿using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VideoSources.DTO;
using AForge.Video;
using AForge.Video.DirectShow;
using System.Timers;

namespace VideoSources
{
    public class VideoSourcesService
    {
        public event EventHandler SourcesChanged;
        private object _locker = new object(); 
        private List<VideoDeviceDTO> _videoSourceList = new List<VideoDeviceDTO>();
        private Timer videoSourcesTimer;

        public VideoSourcesService()
        {
            videoSourcesTimer = new Timer(500);
            videoSourcesTimer.Elapsed += getVideoSources;
            videoSourcesTimer.AutoReset = true;
            videoSourcesTimer.Enabled = true;
        }

        public List<VideoDeviceDTO> GetVideoSources()
        {
            lock (_locker)
            {
                return _videoSourceList;
            }
        }

        public void AddOrUpdateVehicleVideoSource(VideoDeviceDTO vdd)
        {
            bool changed = false;
            lock (_locker)
            {
                var vs = _videoSourceList.FirstOrDefault(v => v.VehicleId == vdd.VehicleId);
                if (vs == null)
                {
                    _videoSourceList.Add(vdd);
                    changed = true;
                }
                else
                {
                    if (string.IsNullOrEmpty(vdd.Name) && !string.IsNullOrEmpty(vs.Name))
                    {
                        _videoSourceList.Remove(vs);
                        changed = true;
                    }
                    else if (vs.Name != vdd.Name)
                    {
                        vs.Id = VideoDeviceDTO.GenerateId(vdd.Id, vdd.Name);
                        vs.Name = vdd.Name;
                        changed = true;
                    }
                }
            }
            if (changed)
            {
                SourcesChanged?.Invoke(_videoSourceList, null);
            }
        }

        public void RemoveVehicleVideoSource(VideoDeviceDTO vdd)
        {
            lock (_locker)
            {
                var vname = _videoSourceList.FirstOrDefault(v => v.VehicleId == vdd.VehicleId);
                if (vname != null)
                {
                    _videoSourceList.Remove(vname);
                    SourcesChanged?.Invoke(_videoSourceList, null);
                }
            }
        }

        private bool isRunning = false;
        private void getVideoSources(object source, ElapsedEventArgs e)
        {
            if (isRunning)
            {
                return;
            }
            isRunning = true;
            FilterInfoCollection VideoCaptureDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            bool changed = false;

            lock (_locker)
            {
                List<VideoDeviceDTO> exists = new List<VideoDeviceDTO>();
                foreach (FilterInfo VideoCaptureDevice in VideoCaptureDevices)
                {
                    var vsd = new VideoDeviceDTO()
                    {
                        Name = VideoCaptureDevice.Name,
                        Id = VideoCaptureDevice.MonikerString,
                        Type = SourceType.USB_CAMERA
                    };
                    exists.Add(vsd);
                }
                foreach (VideoDeviceDTO videoCaptureDevice in exists)
                {
                    var found = _videoSourceList.FirstOrDefault(v => v.Name == videoCaptureDevice.Name);
                    if (found == null)
                    {
                        var vsd = new VideoDeviceDTO()
                        {
                            Name = videoCaptureDevice.Name,
                            Id = videoCaptureDevice.Id,
                            Type = SourceType.USB_CAMERA
                        };
                        _videoSourceList.Add(vsd);
                        changed = true;
                    }
                }
                foreach (VideoDeviceDTO device in _videoSourceList.ToList())
                {
                    if (device.Type != SourceType.USB_CAMERA)
                    {
                        continue;
                    }
                    if (!exists.Any(d => d.Name == device.Name))
                    {
                        _videoSourceList.Remove(device);
                        changed = true;
                    }
                }
            }
            isRunning = false;
            if (changed)
            {
                SourcesChanged?.Invoke(_videoSourceList, null);
            }
        }
    }
}
