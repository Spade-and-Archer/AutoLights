using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Threading;
using System.IO;
using System.Runtime.InteropServices;
using System.Media;
using WMPLib;

namespace AutoLights
{
    /*
           Modes:
         * 0: White/Colour
         * 1: Disco 
             */
    public class LogObj
    {
        /*
         * Simplay an object to make writing to the log file easier and duplicated the contents of the log file in the console if the program is in debug mode
         * Purely for debugging purposes, should not raise any errors of it's own and mostly just spits everything it gets into the log file.
         */

        private string filepath;
        private StreamWriter file;
        
        private DateTime Epoch;
        private int maxDetailWidth;
        private string FormatString;
        private string Preface = "";

        public LogObj(string filepath, int maxDetailWidth)
        {
            this.maxDetailWidth = maxDetailWidth;
            this.filepath = filepath;
            this.Epoch = System.DateTime.Now;
            file = new StreamWriter(filepath,true);
            file.AutoFlush = true;
            FormatString = @"{0,10}|{1,10}|{2," + maxDetailWidth.ToString() + "}|{3,5}";
            file.WriteLine("-----------------------Restart----------------------");
            file.WriteLine(string.Format(FormatString, "Date", "Time", "Details", "Hour"));
            WriteLine("Restarted");
        }
        public void AddToPreface(string addedSegment)
        {
            Preface += addedSegment + "-";
        }
        public void WriteLine(string details)
        {
            details = Preface + details;
            int lines = int.Parse(
                Math.Ceiling(
                        double.Parse(details.Length.ToString())/
                        double.Parse(maxDetailWidth.ToString())
                    ).ToString()
                );//Number of lines to write details
            for (int i = 0; i < lines; i++)
            {
                string detailsSubstring = "";
                //Below voids index out of range if on the last line by capturing the rest of the string instead of a substring of specified length
                if(i == lines - 1) //If this is the last line
                    detailsSubstring = details.Substring(i*maxDetailWidth);
                else
                    detailsSubstring = details.Substring(i*maxDetailWidth, maxDetailWidth);
                string newLine = (
                    string.Format(FormatString,
                        DateTime.Now.ToString(@"dd/MM/yy"),
                        DateTime.Now.ToString(@"HH:mm:ss"),
                        detailsSubstring,
                        (DateTime.Now - this.Epoch).TotalHours.ToString())
                        );
                file.WriteLine(newLine);
                Console.WriteLine(newLine);
                
            }
            
        }

    }



    public class Messenger
    {
        /* A simple class that maintains and manages connection to various devices and servers. The commands Send and AddMessage will both add messages to the message list.
         * The function SendMessageList runs continuously in it's own thread to ensure that the device recieves messages in a linear fashion and responses are not confused.
         * This solved problems I was previously having with messages being sent too quickly and the device struggling to handle it.
         * 
         * NOTE: Your message is very important to us and will be sent  in the *order in which it was recieved.* If you have problems with your message not being sent immediately, 
         * know that this is because other functions are sending other messages. Reduce the messages being sent by other functions and the lag will decrease.
         * 
         * NOTE 2: Send() and AddMessage() are the same function. AddMessage is preferred because it more clearly communicates what will happen when you call it. Send is common
         * nomenclature and, when migrating from individually sending messages, more intuitive to call. For this reason, I simply made both functions work and do the same thing
         * 
         * */

        private int port;
        private string IP;
        private ManualResetEvent[] newMessageAlert = new ManualResetEvent[1];
        private System.Threading.Thread MessageThread;
        private List<byte[]> MessageList = new List<byte[]>();
        public LogObj Log;
        public Messenger(string IP, int port, LogObj log)
        {
            Log = log;
            newMessageAlert[0] = new ManualResetEvent(false);
            MessageThread = new System.Threading.Thread(SendMessageList);
            this.IP = IP;
            this.port = port;
            MessageThread.Start();

        }
        private void SendMessageList()
        {
            //This function manages the message list and ensures all messages are sent
            //in the most efficient and orderly fashion. It ensures some commands do
            //not interrupt others, and that all commands are sent in the correct order
            //with minimum delay
            //--------------------------------------------------------------------
            //Opens a socket with the server
            
            Log.WriteLine("trying to Connect");
            System.Net.Sockets.UdpClient udpClient = new System.Net.Sockets.UdpClient(this.IP, this.port);
            Log.WriteLine("Successfully Connected");
            //delay is the time interval in between messages being set. It ensures the connection does not time out.
            int delay = 0;
            while (true)
            {
                //If tdhe delay is too lare, it resets the connection
                if (delay >= 60000)
                {
                    udpClient.Close();
                    udpClient = new System.Net.Sockets.UdpClient(this.IP, this.port);
                    delay = 0;
                }
                //If there is nothing in the mesaage list, it waits for a new message alert, which signals
                //A new item in the message list
                if (MessageList.Count == 0)
                {
                    //Setting the delay
                    delay = WaitHandle.WaitAny(newMessageAlert);
                }
                //if there IS something in the list, it sends the bytes and removes the item.
                else if(MessageList[0] != null)
                {
                    this.SendSingleMessage(udpClient, MessageList[0]);
                    for (int i = 0; i < 2 && MessageList.Count < 2; i++)
                        //It is preferable to send the message 3 times. This ensures it will send it once, and then
                        //send it two more times if there are no other pending messages.
                        this.SendSingleMessage(udpClient, MessageList[0]);
                    MessageList.RemoveAt(0);
                }
            }
        }
        private void SendSingleMessage(System.Net.Sockets.UdpClient Socket, byte[] Data)
        {
            //Sends the first byte
            Socket.Send(new byte[] {
		    Data[0],
		    0x0
        }, 2);
            if (Data.Length > 1)
            {
                //Waits the necessary amount of time before sending the second and third bytes
                Thread.Sleep(100);

                byte P1 = Data[1];
                byte P2;
                if (Data.Length > 2)//In case there are only 2 parts
                    P2 = Data[2];
                else P2 = 0x00;
                Socket.Send(new byte[]{
                P1,
                P2
            }, 2);
            }


        }
        public void AddMessage(byte[] Data)
        {
            if (Data.Length > 3 || Data.Length == 0)
            {
                Log.WriteLine(String.Format("Number of Bytes being sent must be between 1 and 3. You have tried to send {0} bytes in the same message.", Data.Length));
                throw new ArgumentException(
                    String.Format("Number of Bytes being sent must be between 1 and 3. You have tried to send {0} bytes in the same message.", Data.Length), "Data");
            }
            else
            {
                this.MessageList.Add(Data);
                this.newMessageAlert[0].Set();
            }


        }
        public void Send(byte[] Data)
        {
            AddMessage(Data);
        }
    }
    
    public class LightServer
    {
        /*
         * This is a Light *Server* object which contains organized information and codes necessary to interface with a LimitlessLED light server and references to all of the zones that server controls. Because you don't want to change
         * the 'brightness' or 'colour' of the server itself, it donesn't contain any functions to perform any actions. You must instead use this to reference the LightZones and send commands to them.
         * 
         */
        private Messenger sock;
        public LightZone[] Zones = new LightZone[4];
        private int DefaultMode = 0;
        private int DefaultBrightness = 0;
        private int DefaultColour = 0;
        private int DefaultDisco = 0;
        private Dictionary<string, byte[]>[] ZoneCodes;
        public LogObj Log;
        public LightServer(string IP, int port, LogObj log)
        {
            Log = log;
            ZoneCodes = new Dictionary<string, byte[]>[4];
            ZoneCodes[0] = new Dictionary<string, byte[]>();
            ZoneCodes[1] = new Dictionary<string, byte[]>();
            ZoneCodes[2] = new Dictionary<string, byte[]>();
            ZoneCodes[3] = new Dictionary<string, byte[]>();

            ZoneCodes[0].Add("On", new byte[] { 0x45 });
            ZoneCodes[0].Add("Off", new byte[] { 0x46 });
            ZoneCodes[0].Add("White", new byte[] { 0x45, 0xC5 });
            ZoneCodes[0].Add("Night", new byte[] { 0x46, 0xC6 });

            ZoneCodes[1].Add("On", new byte[] { 0x47 });
            ZoneCodes[1].Add("Off", new byte[] { 0x48 });
            ZoneCodes[1].Add("White", new byte[] { 0x47, 0xC7 });
            ZoneCodes[1].Add("Night", new byte[] { 0x48, 0xC8 });

            ZoneCodes[2].Add("On", new byte[] { 0x49 });
            ZoneCodes[2].Add("Off", new byte[] { 0x4A });
            ZoneCodes[2].Add("White", new byte[] { 0x49, 0xC9 });
            ZoneCodes[2].Add("Night", new byte[] { 0x4A, 0xCA });

            ZoneCodes[3].Add("On", new byte[] { 0x4B });
            ZoneCodes[3].Add("Off", new byte[] { 0x4C });
            ZoneCodes[3].Add("White", new byte[] { 0x4B, 0xCB });
            ZoneCodes[3].Add("Night", new byte[] { 0x4C, 0xCC });

            sock = new Messenger(IP, port, Log);
            for (int i = 0; i < 4; i++)
                this.Zones[i] = new LightZone(ZoneCodes[i], sock.Send, Log);
        }
    }

    public class LightZone
    {
        /*
         * The LightZone is the main object used to control LimitlessLED lights. It can be used to set Brightness or Colour of the zone using the SetBrightness and SetColour commands respectively. SetColor is also a command because
         * why not.
         * 
         * For brightness: 0 is total darkness,  1 is night mode, 2+ is any other brightness setting (max is 12)
         * 
         * Colour is crazy.  Colour 0 is white, other than that I have no idea how this thing interpretets colour. It can be any value between 1 and 256 and it is not Hue in an HSV model (probably because that would make too much sense).
         * I can't find a colour model that corresponds to the crazy model this uses and I have given up. Just try different ones and see what it looks like to you. 
         * 
         * DiscoMode is not actually supported yet.
         
         
         */
        private int DiscoMode = 0;
        private int Brightness = 0;
        private int Colour = 0;
        public string name;//throwaway that probably needs improved support. Just sits and does nothing
        private bool BrightnessChangePending;
        private bool ColourChangePending;
        private bool DiscoChangePending;
        private Action<byte[]> Send;
        private Dictionary<string, byte[]> codes;
        public LogObj Log;

        public LightZone(Dictionary<string, byte[]> givenCodes, Action<byte[]> sender, LogObj log)
        {
            Log = log;
            Send = sender; //Sender is something that will handles sending messages. It will almost certainly be connected to a messenger.Send  command.
            codes = givenCodes;
        }
        public int SetBrightness(int NewBrightness)
        {
            if (DiscoMode == 0)//DiscoMode 0 means normal, DiscoMode 1 is disco
            {//If mode is not zero, brightness is irrelivent.
                if (NewBrightness == 0)
                    Send(codes["Off"]);
                else if (NewBrightness == 1)
                    Send(codes["Night"]);
                else if (NewBrightness >= 2)
                {
                    if (ColourChangePending)
                    {
                        this.SetColour(Colour);
                    }
                    Send(new byte[3] { codes["On"][0], 0x4E, BitConverter.GetBytes(NewBrightness)[0] }); //Change Brightness
                }
                else
                {
                    Log.WriteLine("Illegal brighness change detected. Brightness cannot be a negative number. Continuing without throwing error.")
                }
            }
            else
            {
                BrightnessChangePending = true;//If something else changes that would cause this brightness change to be important (like it turns on
            }
            Brightness = NewBrightness;
            return Brightness;
        }

        public int SetColour(int NewColour)
        {
            /*
             * NewColour is an int between 0 and 256 identifying a colour. I cannot find a model that accurately predicts what colour you will get on your lights (it does not correspond to Hue in HSV) so you're on your own there. You will just 
             * have to do it empirically.
             */
            if (DiscoMode == 0 && Brightness > 1)
            {
                if (NewColour == 0)
                {
                    Send(codes["White"]);
                    SetBrightness(Brightness);
                }
                else if (NewColour >= 0)
                {
                    Send(new byte[3]{codes["On"][0],0x40, 
                        BitConverter.GetBytes(NewColour)[0]});
                }
            }
            else
            {
                ColourChangePending = true;
            }
            Colour = NewColour;
            return Colour;
        }

        public int SetColor(int NewColor)
        {
            return SetColour(NewColor);
        }
    }

    
    public class MusicDriver
    {

        //This is still very much WIP.
        private ManualResetEvent NewSongAlert = new ManualResetEvent(false);
        private ManualResetEvent PauseAlert = new ManualResetEvent(false);
        private ManualResetEvent SongRemovedAlert = new ManualResetEvent(false);
        private ManualResetEvent PlayheadMovedAlert = new ManualResetEvent(false);
        private Random rand = new Random();

        private bool SongLoaded = false;


        public WMPLib.WindowsMediaPlayer wplayer;

        private TimeSpan LastPlayhead = new TimeSpan();
        private DateTime LastPlayTime = new DateTime();

        private bool playing = true; 
        public bool Playing { get {return playing;} 

            protected set
            {
                if (playing != value)
                    PauseAlert.Set();
                PauseAlert.Reset();
                playing = value;
            }
        }

        public bool shuffle = false;
        public bool loop = false;

        private List<Song> MasterQueue = new List<Song>();
        private List<Song> PlayedSongs = new List<Song>();

        private StringBuilder returnData;
        [DllImport("winmm.dll")]
        private static extern long mciSendString(string lpstrCommand, StringBuilder lpstrReturnString, int uReturnLength, int hwndCallback);
        [DllImport("winmm.dll")]
        private static extern int mciSendString(string strCommand, StringBuilder strReturn, int iReturnLength, IntPtr hwndCallback);
        [DllImport("winmm.dll")]
        public static extern int mciGetErrorString(int errCode, StringBuilder errMsg, int buflen);
        [STAThread]
        [DllImport("winmm.dll")]
        public static extern int waveOutGetVolume(IntPtr hwo, out uint dwVolume);
        [DllImport("winmm.dll")]
        public static extern int waveOutSetVolume(IntPtr hwo, uint dwVolume);
        
        private void PlayerEventHandler(int NewState)
        {
            switch (NewState)
            {
                case 8://Media has ended
                    MasterQueue.RemoveAt(0);
                    LoadNextSong();
                    break;
                case 2:
                    break;
                    
            }
                
        }
        public MusicDriver()
        {
            //wplayer.PlayStateChange += new WMPLib._WMPOCXEvents_PlayStateChangeEventHandler(ThreadedTask);
            wplayer = new WMPLib.WindowsMediaPlayer();
            wplayer.PlayStateChange +=
                new WMPLib._WMPOCXEvents_PlayStateChangeEventHandler(PlayerEventHandler);
            wplayer.MediaError += new WMPLib._WMPOCXEvents_MediaErrorEventHandler(Error);       
        }

        public void Error(object errorCode)
        {
            Console.WriteLine("ERROR ERROR " + errorCode.ToString());
        }



        //Controls:
        public void Reset()
        {
            PlayedSongs.Clear();
            MasterQueue.Clear();
            wplayer.controls.stop();
            SongLoaded = false;
        }

         public void Pause()
        {
            if (Playing)
            {
                wplayer.controls.pause();
                LastPlayhead += (DateTime.Now - LastPlayTime);
                Playing = false;
            }
            else
            {
                wplayer.controls.pause();
            }

        }

        public void Play()
        {
            if (!Playing)
            {

                Seek(LastPlayhead);//Make sure Playhead is in correct position
                wplayer.controls.play();
                LastPlayTime = DateTime.Now;
                Playing = true;
            }
        }

        public void Seek(TimeSpan Pos) {
            //if(Playing)
            wplayer.controls.currentPosition = Pos.TotalMilliseconds;
            //    mciSendString("play MediaFile from " + Pos.TotalMilliseconds.ToString(), null, 0, IntPtr.Zero);
            //else
            //    mciSendString("seek MediaFile to "  + Pos.TotalMilliseconds.ToString(), null, 0, IntPtr.Zero);
            LastPlayhead = Pos;
            LastPlayTime = DateTime.Now;
        } 

        //Load overrides:
        public void Load(Song target)
        {
            //forcestart if a song is not loaded
            Load(target, (!SongLoaded), -1);
        }
        public void Load(Song target, bool forceStart)
        {
            Load(target, forceStart, -1);
        }
        public void Load(Song target, int index)
        {
            Load(target, (!SongLoaded), index);
        }
        public void Load(Song[] target)
        {
            //forcestart if a song is not loaded
            Load(target, (!SongLoaded), -1);
        }
        public void Load(Song[] target, bool forceStart)
        {
            Load(target, forceStart, -1);
        }
        public void Load(Song[] target, int index)
        {
            Load(target, (!SongLoaded), index);
        }
        public void Load(Song target, bool forceStart, int index)
        {
            Load(new Song[] { target }, forceStart, index);
        }
        public void Load(Song[] target, bool forceStart, int index)//index of -1 indicates appending song to end
        {
            /*
             * The load command loads a song or list of songs and places them at position 'index' in the current play order. If, for example, index = 1 and a list of 5 songs are being added, the current song will finish
             * song zero in the current playlist will play, and then the five songs being loaded will play, followed by the song that was previously in poistion 1. If the index is -1 or too high, the song will be appended to 
             * the end of the queue. If forceStart is true, then the player will insert the songs into the queue at the designated index, and then skip all of the songs in the queue so that the songs being loaded are next.
             * It will then skip the remainder of the currently playing song to immediately play the song(s) being loaded. 
             * */
            //Close();//Close current song if there is one playing;
            // Try to open as mpegvideo
            if(index ==-1){
                    index = wplayer.currentPlaylist.count;
            }
            else if (index < -1){
                throw Exception new IndexOutOfRangeException("Cannot put a song in a negative queue position. -1 indicates append song to end, but any other negative argument is illegal");
            }
            for (int i = 0; i < target.Length; i++)//insert each item in target list one after another
            {
                if (index + i > 0 && index + i < wplayer.currentPlaylist.count)
                    wplayer.currentPlaylist.insertItem(index + i, target[i].Media);
                else
                    wplayer.currentPlaylist.appendItem(target[i].Media);
            }

            if (forceStart)
            {
                
                for (int i = 0; i < index; i++)
                    wplayer.currentPlaylist.removeItem(wplayer.currentPlaylist.get_Item(0));//remove all items between index 0 and index i
                wplayer.controls.playItem(wplayer.currentPlaylist.get_Item(0));
            }
            
            //string command = "open \"" + target.Path +
            //           "\" type mpegvideo alias MediaFile";
            //int error = mciSendString(command, null, 0, IntPtr.Zero);
            //if (error != 0)
            //{
                // Let MCI deside which file type the song is
                //command = "open \"" + target.Path +
                //           "\" alias MediaFile";
                //error = mciSendString(command, null, 0, IntPtr.Zero);
                
            //}
            //mciSendString("play MediaFile", null, 0, IntPtr.Zero);
            //mciSendString("resume MediaFile", null, 0, IntPtr.Zero);
       }
       


        public TimeSpan GetSongLength()
        {
            if (SongLoaded)
            {
                string command = "status MediaFile length";
                mciSendString(command, returnData, returnData.Capacity, IntPtr.Zero);
                return new TimeSpan(0,0,0,0,int.Parse(returnData.ToString()));
            }
            else
                return new TimeSpan(0,0,0);

        }


        
        private void LoadNextSong()
        {
            if (MasterQueue.Count == 0 && loop == true && PlayedSongs.Count > 0)
            {
                MasterQueue = PlayedSongs;
                Load(MasterQueue.ToArray(),true);
            }
            else if (MasterQueue.Count == 0)
            {
                WaitHandle.WaitAny(new ManualResetEvent[1] { NewSongAlert });
                wplayer.controls.play();
            }
            
            
            
        }
        
        public void ClearQueue() 
        {
            MasterQueue.Clear();
            SongRemovedAlert.Set();
            SongRemovedAlert.Reset();
            wplayer.controls.stop();
            wplayer.currentPlaylist.clear();
        }

        
        
        

    }
    
    public class Song
    {
        public string path;
        public string Path { get { return path; } protected set { path = value; } }
        public SoundPlayer Player;
        public string title;
        public string Title { get { return title; } protected set { title = value; } }
        public string Artist { get { return Artist; } protected set { Artist = value; } }
        public TimeSpan duration;
        private IWMPMedia media;
        public IWMPMedia Media {get {return media;} protected set {Media = media;}}
        public TimeSpan Duration{get{return duration;} protected set{duration = value;} }
        public Song(string path, WMPLib.WindowsMediaPlayer player) 
        {

            Path = path;
            Duration = new TimeSpan(0, 4, 0);
            media = player.newMedia(path);
        }

    } 
    
    public class MusicZone
    {
        //Just a placeholder for later
        private string[] Songs;
        private bool Playing = false;
        private string[] Queue;
        private int QueuePos;
    }
   
    public class AutoController
        /*
         * This class is given a list of events, and a zone that the events are happening in. When threadedTask is run in it's own thread, it will silently trigger the events in the event list and pass them the EditingZone to trigger in.
         
         */
        {
            private LightZone EditingZone; //support for music zones is coming.
            private List<Event> ScheduledEvents = new List<Event>();
            public AutoController(LightZone editingZone, List<Event> events)
            {
                EditingZone = editingZone;
                ScheduledEvents = events;
            }
            private void sortTimes()//Sort the events from earliest to latest
            {
                List<Event> newScheduledEvents = new List<PsuedoRandomLightTimings>();
                while(ScheduledEvents.Count > 0)//sorts with efficiency n*n/2. Since it is a small list and likely already mostly ordered, this will likely be ~n
                {
                    Event minInterval = ScheduledEvents[0];
                    int minPos = 0;
                    for (int carat = 1; carat < ScheduledEvents.Count; carat++)
                    {
                        if (ScheduledEvents[carat].EventStartTime < minInterval.EventStartTime)
                        {
                            minInterval = ScheduledEvents[carat];
                            minPos = carat;
                        }

                    }
                    ScheduledEvents.RemoveAt(minPos);
                    newScheduledEvents.Add(minInterval);
                }
                ScheduledEvents = newScheduledEvents;

            }
            public void ThreadedTask()
            {
                sortTimes();
                while (true){
                    try
                    {
                        sortTimes();//ensure that all Scheduled events are in order from earliest to latest
                        for (int i = 0; i < ScheduledEvents.Count; i++)
                        {
                            DateTime Now = new DateTime(1, 1, 1, DateTime.Now.Hour, DateTime.Now.Minute, DateTime.Now.Second);
                            if (ScheduledEvents[i].EventStartTime > Now) //if the next scheduled event is later, wait until it is time
                                Thread.Sleep(ScheduledEvents[i].EventStartTime - Now);
                            if (ScheduledEvents[i].EventStartTime <= Now && ScheduledEvents[i].EventStartTime + ScheduledEvents[i].TotalDuration + new TimeSpan(0, 0, 3) > Now)//When it is time for (or within three seconds of) the event 
                                ScheduledEvents[i].Begin(EditingZone);


                        }
                    }
                    catch
                    { Console.WriteLine("Unkown Error occured. Restarting Thread"); }
                    
                }
                
            }

        }

    public class Event
    {
        //Placehold event with Begin action
        public DateTime EventStartTime;
        public TimeSpan TotalDuration;
        public void Begin(LightZone activeZone){

        }
    }
    public class PsuedoRandomLightTimings:Event
    {
        /*
         * A light event that automatically turns lights on and off for pseudo random intervals of time to immitate the home owners presence. This is my immediate use case, so 
         * it is currently the only event type.
         
         */
        private Random RandGenerator;
        private TimeSpan MinOnDuration;
        private TimeSpan MaxOnDuration;
        private TimeSpan MinOffDuration;
        private TimeSpan MaxOffDuration;
        private int OnBrightness;
        private bool TurnOffAfter = true;//If there is a followup event, it should not turn lights off after it finishes
        private bool StayOnEntireDuration=false;

        //A bunch of overrides to generate an event
        public PsuedoRandomLightTimings(DateTime start, DateTime end, TimeSpan minStateDuration, TimeSpan maxStateDuration, int brightness){
            MasterInitiator(start, end - start, minStateDuration, maxStateDuration, minStateDuration, maxStateDuration, brightness);
        }
        public PsuedoRandomLightTimings(DateTime start, TimeSpan totalDuration, TimeSpan minOnDuration, TimeSpan maxOnDuration, TimeSpan minOffDuration, TimeSpan maxOffDuration, int brightness)
        {
            MasterInitiator(start, totalDuration, minOnDuration, maxOnDuration, minOffDuration, maxOffDuration, brightness);
        }
        public PsuedoRandomLightTimings(DateTime start, DateTime end, TimeSpan minOnDuration, TimeSpan maxOnDuration, TimeSpan minOffDuration, TimeSpan maxOffDuration, int brightness)
        {
            MasterInitiator(start, end-start, minOnDuration, maxOnDuration, minOffDuration, maxOffDuration, brightness);
        }
        public PsuedoRandomLightTimings(DateTime start, DateTime end, bool StayOn, int brightness)
        {
            StayOnEntireDuration = StayOn;
            TotalDuration = end - start;
            MasterInitiator(start, end - start, TotalDuration, TotalDuration, new TimeSpan(0, 0, 0), new TimeSpan(0, 0, 0), brightness);
        }
        public PsuedoRandomLightTimings(DateTime start, TimeSpan totalDuration, bool StayOn, int brightness)
        {
            if (!StayOn)
            {
                throw new ArgumentException("Cannot declare StayOn as false. Must instead specify min/max state durations.");
            }
            StayOnEntireDuration = StayOn;
            TotalDuration = totalDuration;
            MasterInitiator(start, totalDuration, TotalDuration, TotalDuration, new TimeSpan(0, 0, 0), new TimeSpan(0, 0, 0), brightness);
        }
        
        public void FollowUpEvent(bool isFollowup){
            TurnOffAfter = !isFollowup;
        }

        private void MasterInitiator(DateTime start, TimeSpan totalDuration,TimeSpan minOnDuration, TimeSpan maxOnDuration, TimeSpan minOffDuration, TimeSpan maxOffDuration, int brightness)
        {
            MinOnDuration = minOnDuration;
            MaxOnDuration = maxOnDuration;
            MinOffDuration = minOffDuration;
            MaxOffDuration = maxOffDuration;
            EventStartTime = start;
            TotalDuration = totalDuration;
            //If this is supposed to run from some time at night until some time in the morning, the duration will appear negative
            //e.g. start = 20:00, end = 5:00 --> 5:00 - 20:00 = -15:00.
            //This event should not last -15 hours, it should last 9 hours. By adding 24, it corrects the problem:
            //e.g. 
            //      start = 20:00, end = 5:00 --> 5:00 - 20:00 = -15:00
            //      -15:00 != |-15:00| so:
            //          -15:00 += 24:00
            //      duration = 9:00
            if (TotalDuration != TotalDuration.Duration())//If the duration is not equal to the absolute value of the duration (if it is negative)
            {
                TotalDuration += new TimeSpan(1, 0, 0, 0);//Add one day
            }
            OnBrightness = brightness;
            RandGenerator = new Random((int)((MaxOnDuration.TotalSeconds - MinOnDuration.TotalMilliseconds) * TotalDuration.TotalMinutes));
            //The above randGenerator has a seed which (should) ensure few, if any, repeats among different Intervals
        }
       
        public override void Begin(LightZone activeZone){
            TimeSpan timeRemaining = TotalDuration - (new DateTime(1,1,1,DateTime.Now.Hour, DateTime.Now.Minute, DateTime.Now.Second) - EventStartTime);
            bool turningOn = true;
            TimeSpan max, min;
            int newbrightness;
            /*
             So long as the there is enough time for the minimum 'on' period to elapse, it will cylce. The first cycle will be a turn-on,
             * and it will set the max and min to the maximum and minimum 'on' periods. Then, it ensures that the maximum will not
             * cause the event to exceed the maximum event duration. Then, it calculates a random number of seconds
             * between the maximum and minimum previously defined and sleeps for that amount of time. Finally, it reverses
             * the value of 'turningOn', so that it proceeds to set the max and min to the maximum and minimum 'off' times and
             * changes the value of 'newBrightness' to shut off the lights instead of turning them on.
             */
            while(timeRemaining > MinOnDuration || (StayOnEntireDuration && timeRemaining > new TimeSpan(0,0,10))){
                if (turningOn)
                {
                    max = MaxOnDuration;
                    min = MinOnDuration;
                    newbrightness = OnBrightness;
                }
                else
                {
                    max = MaxOffDuration;
                    min = MinOffDuration;
                    newbrightness = 0;
                }
                TimeSpan realmax = max;
                if (timeRemaining < max)
                    realmax = timeRemaining; // The maximum time must always be less than or equal to the time remaining (no OT)
                int secondsToSleep;
                if (min < realmax)
                    secondsToSleep = RandGenerator.Next((int)(min.TotalSeconds), (int)(realmax.TotalSeconds));
                else if (min.TotalSeconds > 0) secondsToSleep = (int)min.TotalSeconds;
                else break;

                //Finds seconds to sleep as a random number between the minimum sleep time and the maximum sleep time.
                //It finds seconds (not milliseconds) so that the limit on integer size is not exceeded
                string onORoff;
                if(turningOn) onORoff = "on";
                else onORoff = "off";
                activeZone.Log.WriteLine(String.Format(
                    "Turning {0} {1} lights",
                    onORoff,
                    activeZone.name
                    ));
                activeZone.SetBrightness(newbrightness);
                Thread.Sleep(new TimeSpan(0, 0, secondsToSleep));
                turningOn = !turningOn; //Switches TurningOn (if true, it is now false. if false, it is now true)
                timeRemaining = TotalDuration - (new DateTime(1, 1, 1, DateTime.Now.Hour, DateTime.Now.Minute, DateTime.Now.Second) - EventStartTime);
            }
            if(TurnOffAfter)
                activeZone.SetBrightness(0); 
        }
    }
    class Pogram
    {

        [DllImport("winmm.dll")]
        private static extern long mciSendString(string strCommand,
                                                 StringBuilder strReturn,
                                                 int iReturnLength,
                                                 IntPtr hwndCallback);
        [STAThread]
        static void Main(string[] args)
        {
            Controller theController = new Controller();
            

        }
    }
    class Controller
    {
        public LogObj Log;

        static DateTime time(int hours, int minutes)
        {
            return new DateTime(1, 1, 1, hours, minutes, 0);
        }
        static DateTime time(int hours)
        {
            return time(hours, 0);
        }
         static TimeSpan span(double hours)
        {
            return new TimeSpan((int)Math.Floor(hours), (int)((hours - Math.Floor(hours)) * 60), 0);
        }
        public Controller()
        {
            Log = new LogObj(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) + "//Log.txt", 40);
            PsuedoRandomLightTimings NormalRoomActivity = new PsuedoRandomLightTimings(time(8), time(22), span(.5), span(1.5), 14);
            PsuedoRandomLightTimings BathroomActivity = new PsuedoRandomLightTimings(time(0), time(23), span(.1), span(.25), span(.5), span(2), 14);
            PsuedoRandomLightTimings OutdoorLightsP1 = new PsuedoRandomLightTimings(time(16), time(22, 30), true, 17);
            OutdoorLightsP1.FollowUpEvent(true);//There is a followup event
            PsuedoRandomLightTimings OutdoorLightsP2 = new PsuedoRandomLightTimings(time(22,30), time(4, 30), true, 1);
            OutdoorLightsP2.FollowUpEvent(true);//There is a followup event
            PsuedoRandomLightTimings OutdoorLightsP3 = new PsuedoRandomLightTimings(time(4, 30), time(8), true, 17);
            
            LightServer MainServer = new LightServer("192.168.1.68", 8899, Log);
            MusicDriver MainSound = new MusicDriver();
            Song testingSong = new Song(@"C:/Users/Sam/Music/D&D/Background Music/Hero Down.mp3", MainSound.wplayer);
            for (int i = 0; i < 10; i++ )
                MainSound.Load(testingSong);
            MainSound.Play();



            MainServer.Zones[2].name = "Bathroom";
            MainServer.Zones[1].name = "Normal Rooms";
            MainServer.Zones[0].name = "Outside";

            MainServer.Zones[0].SetBrightness(10);
            MainServer.Zones[1].SetBrightness(10);
            MainServer.Zones[2].SetBrightness(10);
            MainServer.Zones[3].SetBrightness(10);
            
            AutoController BathroomManager = new AutoController(MainServer.Zones[2], new List<Event> { BathroomActivity });
            AutoController NormalRoomManager = new AutoController(MainServer.Zones[1], new List<Event> { NormalRoomActivity });
            AutoController OutdoorManager = new AutoController(MainServer.Zones[0], new List<Event> {
                OutdoorLightsP1,
                OutdoorLightsP2,
                OutdoorLightsP3
            });
            Thread BathroomThread = new Thread(BathroomManager.ThreadedTask);
            BathroomThread.Name = "Bathroom";
            Thread NormalRoomThread = new Thread(NormalRoomManager.ThreadedTask);
            NormalRoomThread.Name = "Hall";
            Thread OutdoorThread = new Thread(OutdoorManager.ThreadedTask);
            OutdoorThread.Name = "Outdoor";
            int consequtiveErrors = 0;
            
                    
                    BathroomThread.Start();
                    NormalRoomThread.Start();
                    OutdoorThread.Start();
                    consequtiveErrors = 0;

                    
                    
             
            
        }
    }
}
