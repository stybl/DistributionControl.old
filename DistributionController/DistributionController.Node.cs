﻿namespace DistributionController
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    using Newtonsoft.Json;

    internal sealed class Node
    {
        public readonly DistributionCommon.Schematic.Node Schematic;
        private NetClient client;
        private Thread watchdog;
        private System.Timers.Timer timeoutTimer;
        private AssignedJobGetter assignedJobs;
        private int pingDelay;

        public Node(DistributionCommon.Schematic.Node schematic, LostNodeHandler lostHandler, RecoveredNodeHandler recoveredHandler, TimeoutHandler timeoutHandler, AssignedJobGetter jobGetter, int pingDelay)
        {
            this.Schematic = schematic;
            this.Reachable = false;
            this.client = new NetClient(this.Schematic.Address, this.Schematic.Port, this.RequestFailedHandler);
            this.Awake = true;
            this.pingDelay = pingDelay;

            this.watchdog = new Thread(() => this.PingLoop());
            this.watchdog.Start();

            Thread.Sleep(pingDelay);
            if (this.Reachable)
            {
                if (this.Status())
                {
                    this.Reset();
                }

                this.Construct();
            }
            else
            {
                this.watchdog.Abort();
                throw new InitializationException();
            }

            this.LostNode += lostHandler;
            this.RecoveredNode = recoveredHandler;
            this.Timeout += timeoutHandler;
            this.assignedJobs = jobGetter;
        }

        public delegate void LostNodeHandler(Node sender, EventArgs e);

        public delegate void RecoveredNodeHandler(Node sender, EventArgs e);

        public delegate void TimeoutHandler(Node sender, EventArgs e);

        public delegate List<Job> AssignedJobGetter(Node sender);

        public event LostNodeHandler LostNode;
        
        public event RecoveredNodeHandler RecoveredNode;

        public event TimeoutHandler Timeout;

        public bool Reachable { get; private set; }

        public bool Awake { get; private set; }
        
        public List<Job> AssignedJobs
        {
            get
            {
                return this.assignedJobs(this);
            }
        }

        public bool Assign(Job job)
        {
            var request = new DistributionCommon.Comm.Requests.Assign(job.Blueprint);
            var response = this.SendRequest<DistributionCommon.Comm.Responses.Assign>(request);
            if (response != default(DistributionCommon.Comm.Responses.Assign))
            {
                return response.Success;
            }

            return false;
        }

        public bool Construct()
        {
            var request = new DistributionCommon.Comm.Requests.Construct(this.Schematic);
            var response = this.SendRequest<DistributionCommon.Comm.Responses.Construct>(request);
            if (response != default(DistributionCommon.Comm.Responses.Construct))
            {
                return response.Success;
            }

            return false;
        }

        public bool Remove(int id)
        {
            var request = new DistributionCommon.Comm.Requests.Remove(id);
            var response = this.SendRequest<DistributionCommon.Comm.Responses.Remove>(request);
            if (response != default(DistributionCommon.Comm.Responses.Remove))
            {
                return response.Success;
            }

            return false;
        }

        public bool Reset()
        {
            var request = new DistributionCommon.Comm.Requests.Reset();
            var response = this.SendRequest<DistributionCommon.Comm.Responses.Reset>(request);
            if (response != default(DistributionCommon.Comm.Responses.Reset))
            {
                return response.Success;
            }

            return false;
        }

        public bool SleepJob(int id)
        {
            var request = new DistributionCommon.Comm.Requests.Sleep(id);
            var response = this.SendRequest<DistributionCommon.Comm.Responses.Sleep>(request);
            if (response != default(DistributionCommon.Comm.Responses.Sleep))
            {
                return response.Success;
            }

            return false;
        }

        public bool Status()
        {
            var request = new DistributionCommon.Comm.Requests.Status();
            var response = this.SendRequest<DistributionCommon.Comm.Responses.Status>(request);
            if (response != default(DistributionCommon.Comm.Responses.Status))
            {
                return response.Constructed;
            }

            return false;
        }

        public bool WakeJob(int id)
        {
            var request = new DistributionCommon.Comm.Requests.Wake(id);
            var response = this.SendRequest<DistributionCommon.Comm.Responses.Wake>(request);
            if (response != default(DistributionCommon.Comm.Responses.Wake))
            {
                return response.Success;
            }

            return false;
        }

        public bool Sleep()
        {
            this.InterruptCountdown();
            this.watchdog.Abort();
            this.Awake = false;
            return this.Reset();
        }

        public bool Wake()
        {
            this.watchdog = new Thread(() => this.PingLoop());
            this.watchdog.Start();
            this.Awake = true;
            return this.Construct();
        }

        public void BeginCountdown(int duration)
        {
            this.timeoutTimer = new System.Timers.Timer(Convert.ToDouble(duration));
            this.timeoutTimer.AutoReset = false;
            this.timeoutTimer.Elapsed += (s, e) => { this.OnTimeout(EventArgs.Empty); };
            this.timeoutTimer.Start();
        }

        public void InterruptCountdown()
        {
            if (this.timeoutTimer.Enabled)
            {
                this.timeoutTimer.Stop();
            }

            this.timeoutTimer = null;
        }

        private void OnLostNode(EventArgs e)
        {
            if (this.LostNode != null)
            {
                this.LostNode(this, e);
            }
        }

        private void OnRecoveredNode(EventArgs e)
        {
            if (this.RecoveredNode != null)
            {
                this.RecoveredNode(this, e);
            }
        }

        private void OnTimeout(EventArgs e)
        {
            if (this.Timeout != null)
            {
                this.Timeout(this, e);
            }
        }

        private void RequestFailedHandler(EventArgs e)
        {
            if (this.Reachable)
            {
                this.Reachable = false;
                this.OnLostNode(e);
            }
        }

        private T SendRequest<T>(DistributionCommon.Comm.Requests.Base request)
        {
            try
            {
                var settings = new JsonSerializerSettings();
                settings.MissingMemberHandling = MissingMemberHandling.Error;

                string responseString = this.client.Send(JsonConvert.SerializeObject(request));
                if (responseString != DistributionCommon.Constants.Communication.InvalidRequestResponse)
                {
                    var baseResponse = JsonConvert.DeserializeObject<DistributionCommon.Comm.Responses.Base>(responseString);
                    if (baseResponse.ResponseType == typeof(T))
                    {
                        return JsonConvert.DeserializeObject<T>(responseString);
                    }
                }

                throw new JsonException();
            }
            catch (JsonException)
            {
                return default(T);
            }
        }

        private void PingLoop()
        {
            while (true)
            {
                bool success = false;
                try
                {
                    var client = new TcpClient();
                    success = client.BeginConnect(this.Schematic.Address, this.Schematic.Port, null, null).AsyncWaitHandle.WaitOne(delay);
                    client.Close();
                }
                catch (SocketException)
                {
                }
                finally
                {
                    if (success)
                    {
                        if (!this.Reachable)
                        {
                            this.Reachable = true;
                            this.OnRecoveredNode(EventArgs.Empty);
                        }
                    }
                    else
                    {
                        if (this.Reachable)
                        {
                            this.OnLostNode(EventArgs.Empty);
                            this.Reachable = false;
                        }
                    }
                }

                Thread.Sleep(this.pingDelay);
            }
        }

        internal sealed class InitializationException : Exception
        {
            public InitializationException() : base()
            {
            }
        }
    }
}
