using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VideoSources.DTO;

namespace VideoSources
{
    public class VideoSourcesService
    {
        public event EventHandler SourcesChanged;
        private object _locker = new object(); 
        private List<VideoSourceDTO> _videoSourceList = new List<VideoSourceDTO>();

        public VideoSourcesService()
        {
            _videoSourceList.Add(new VideoSourceDTO()
            {
                Name = "USB Capture HDMI"
            });
            _videoSourceList.Add(new VideoSourceDTO()
            {
                Name = "Webcam C170"
            });
        }

        public VideoSourcesService(string devices)
        {
            try
            {
                List<string> str = devices.Split(';').ToList();
                foreach (var dev in str)
                {
                    _videoSourceList.Add(new VideoSourceDTO()
                    {
                        Name = dev
                    });
                }
            } catch (Exception e)
            {
                throw e;
            }
        }

        public List<VideoSourceDTO> GetVideoSources()
        {
            lock (_locker)
            {
                return _videoSourceList;
            }
        }

        private unsafe void GetVideoSourcesFFmpeg()
        {
            ffmpeg.avdevice_register_all();
            ffmpeg.avformat_network_init();
            ffmpeg.avcodec_register_all();
            try
            {
                AVInputFormat* iformat = ffmpeg.av_find_input_format("dshow");
                AVFormatContext* s = ffmpeg.avformat_alloc_context();
                AVDeviceInfoList* device_list = null;
                s->iformat = iformat;
                if (s->iformat->priv_data_size > 0)
                {
                    s->priv_data = ffmpeg.av_mallocz((ulong)s->iformat->priv_data_size);
                    /*if (!s->priv_data)
                    {
                        avformat_free_context(s);
                        return devices;
                    }
                    if (s->iformat->priv_class)
                    {
                        *static_cast <const AVClass**> (s->priv_data) = s->iformat->priv_class;
                        av_opt_set_defaults(s->priv_data);
                    }
                    */
                }
                else
                {
                    s->priv_data = null;
                }
                s->priv_data = null;
                ffmpeg.avdevice_list_devices(s, &device_list);
            }
            catch (Exception e)
            {
                int r = 1;
            }
            int x = 1;
        }
    }
}
