﻿//******************************************************************************************************
//  SnmpHost.cs - Gbtc
//
//  Copyright © 2020, Grid Protection Alliance.  All Rights Reserved.
//
//  Licensed to the Grid Protection Alliance (GPA) under one or more contributor license agreements. See
//  the NOTICE file distributed with this work for additional information regarding copyright ownership.
//  The GPA licenses this file to you under the MIT License (MIT), the "License"; you may not use this
//  file except in compliance with the License. You may obtain a copy of the License at:
//
//      http://opensource.org/licenses/MIT
//
//  Unless agreed to in writing, the subject software distributed under the License is distributed on an
//  "AS-IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. Refer to the
//  License for the specific language governing permissions and limitations.
//
//  Code Modification History:
//  ----------------------------------------------------------------------------------------------------
//  09/10/2020 - J. Ritchie Carroll
//       Generated original version of source code.
//
//******************************************************************************************************

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using GSF;
using GSF.Data;
using GSF.Diagnostics;
using GSF.IO;
using GSF.Net.Snmp;
using GSF.Net.Snmp.Messaging;
using GSF.Net.Snmp.Pipeline;
using GSF.Net.Snmp.Security;
using GSF.Parsing;
using GSF.Threading;
using GSF.TimeSeries;
using GSF.TimeSeries.Adapters;
using ConnectionStringParser = GSF.Configuration.ConnectionStringParser<GSF.TimeSeries.Adapters.ConnectionStringParameterAttribute>;

// ReSharper disable PossibleMultipleEnumeration
namespace SIMProx
{
    /// <summary>
    /// Represents an event triggering adapter.
    /// </summary>
    [Description("SNMP Proxy Host: Triggers Agent Forwarding and Database Operations from V3 SNMP Traps.")]
    public class SnmpHost : FacileActionAdapterBase
    {
        #region [ Members ]

        // Constants
        private const string DefaultConfigFileName = "simprox.xml";
        private const string DefaultDatabaseConnectionString = "";
        private const string DefaultDatabaseProviderString = "AssemblyName={System.Data, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089}; ConnectionType=System.Data.SqlClient.SqlConnection; AdapterType=System.Data.SqlClient.SqlDataAdapter";
        private const string DefaultDatabaseCommand = "sp_LogSsamEvent";
        private const string DefaultDatabaseCommandTemplate = "1,{EventType},'{Flow}','','{Description}',''";
        private const double DefaultDatabaseMaximumWriteInterval = DelayedSynchronizedOperation.DefaultDelay / 1000.0D;
        private const int DefaultFramesPerSecond = 30;
        private const double DefaultLagTime = 5.0D;
        private const double DefaultLeadTime = 5.0D;
        private const ushort DefaultSnmpPort = 162;
        private const bool DefaultForwardingEnabled = false;
        private const string DefaultForwardCommunity = "";
        private const string DefaultForwardIPEndPoint = Snmp.DefaultIPEndPoint;
        private const string DefaultForwardAuthPhrase = "pqgBG80CwgSDMKza";
        private const string DefaultForwardEncryptKey = "EjdEtEhHJCdLM04K";

        // Fields
        private Config m_config;
        private SnmpEngine m_snmpEngine;
        private long m_totalReceivedSnmpTraps;
        private OctetString m_forwardCommunity;
        private DESPrivacyProvider m_forwardPrivacyProvider;
        private IPEndPoint m_forwardIPEndPoint;
        private ConcurrentQueue<string[]> m_commandParameters;
        private DelayedSynchronizedOperation m_databaseOperation;
        private long m_totalDatabaseOperations;
        private object m_lastDatabaseOperationResult;
        private bool m_disposed;

        #endregion

        #region [ Properties ]

        /// <summary>
        /// Gets or sets the SIMProx configuration file name.
        /// </summary>
        [ConnectionStringParameter]
        [Description("Defines the SIMProx configuration file name.")]
        [DefaultValue(DefaultConfigFileName)]
        public string ConfigFileName { get; set; } = DefaultConfigFileName;

        /// <summary>
        /// Gets or sets the connection string used for database operation. Leave blank to use local configuration database defined in "systemSettings".
        /// </summary>
        [ConnectionStringParameter]
        [Description("Defines the connection string used for database operation. Leave blank to use local configuration database defined in \"systemSettings\".")]
        [DefaultValue(DefaultDatabaseConnectionString)]
        public string DatabaseConnnectionString { get; set; } = DefaultDatabaseConnectionString;

        /// <summary>
        /// Gets or sets the provider string used for database operation. Defaults to a SQL Server provider string.
        /// </summary>
        [ConnectionStringParameter]
        [Description("Defines the provider string used for database operation. Defaults to a SQL Server provider string.")]
        [DefaultValue(DefaultDatabaseProviderString)]
        public string DatabaseProviderString { get; set; } = DefaultDatabaseProviderString;

        /// <summary>
        /// Gets or sets the command used for database operation, e.g., a stored procedure name or SQL expression like "INSERT".
        /// </summary>
        [ConnectionStringParameter]
        [Description("Defines the command used for database operation, e.g., a stored procedure name or SQL expression like \"INSERT\".")]
        [DefaultValue(DefaultDatabaseCommand)]
        public string DatabaseCommand { get; set; } = DefaultDatabaseCommand;

        /// <summary>
        /// Gets or sets the command template that includes any desired value substitutions used for database operation. Available substitutions: {Flow} and {Description}.
        /// </summary>
        [ConnectionStringParameter]
        [Description("Defines the command template that includes any desired value substitutions used for database operation. Available substitutions: {EventType}, {Flow} and {Description}.")]
        [DefaultValue(DefaultDatabaseCommandTemplate)]
        public string DatabaseCommandTemplate { get; set; } = DefaultDatabaseCommandTemplate;

        /// <summary>
        /// Gets or sets the maximum interval, in seconds, at which the adapter can execute database operations. Set to zero for no delay.
        /// </summary>
        [ConnectionStringParameter]
        [Description("Defines the maximum interval, in seconds, at which the adapter can execute database operations. Set to zero for no delay.")]
        [DefaultValue(DefaultDatabaseMaximumWriteInterval)]
        public double DatabaseMaximumWriteInterval { get; set; } = DefaultDatabaseMaximumWriteInterval;

        /// <summary>
        /// Gets or sets the number of frames per second.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public new int FramesPerSecond // Redeclared to provide a default value since property is not commonly used
        {
            get => base.FramesPerSecond;
            set => base.FramesPerSecond = value;
        }

        /// <summary>
        /// Gets or sets the allowed past time deviation tolerance, in seconds (can be sub-second).
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public new double LagTime // Redeclared to provide a default value since property is not commonly used
        {
            get => base.LagTime;
            set => base.LagTime = value;
        }

        /// <summary>
        /// Gets or sets the allowed future time deviation tolerance, in seconds (can be sub-second).
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public new double LeadTime // Redeclared to provide a default value since property is not commonly used
        {
            get => base.LeadTime;
            set => base.LeadTime = value;
        }

        /// <summary>
        /// Gets or sets output measurements that the action adapter will produce, if any.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public override IMeasurement[] OutputMeasurements // Redeclared to hide property - not relevant to this adapter
        {
            get => base.OutputMeasurements;
            set => base.OutputMeasurements = value;
        }

        /// <summary>
        /// Gets or sets the SNMP port the proxy host will listen on.
        /// </summary>
        [ConnectionStringParameter]
        [Description("Defines the SNMP port the proxy host will listen on.")]
        [DefaultValue(DefaultSnmpPort)]
        public int SnmpPort { get; set; } = DefaultSnmpPort;

        /// <summary>
        /// Gets or sets flag that determines if SNMP forwarding agent is enabled.
        /// </summary>
        [ConnectionStringParameter]
        [Description("Defines flag that determines if SNMP forwarding agent is enabled.")]
        [DefaultValue(DefaultForwardingEnabled)]
        public bool ForwardingEnabled { get; set; } = DefaultForwardingEnabled;

        /// <summary>
        /// Gets or sets configured SNMP forwarding agent community string to use when forwarding is enabled.
        /// </summary>
        [ConnectionStringParameter]
        [Description("Defines configured SNMP forwarding agent community string to use when forwarding is enabled. Leave blank to forward original source community string.")]
        [DefaultValue(DefaultForwardCommunity)]
        public string ForwardCommunity { get; set; } = DefaultForwardCommunity;

        /// <summary>
        /// Gets or sets configured SNMP forwarding agent IP end point to use when forwarding is enabled.
        /// </summary>
        [ConnectionStringParameter]
        [Description("Defines configured SNMP forwarding agent IP end point to use when forwarding is enabled. Example format: " + DefaultForwardIPEndPoint)]
        [DefaultValue(DefaultForwardIPEndPoint)]
        public string ForwardIPEndPoint { get; set; } = DefaultForwardIPEndPoint;

        /// <summary>
        /// Gets or sets configured SNMP forwarding agent authorization phrase to use when forwarding is enabled. Must be at least 16 characters.
        /// </summary>
        [ConnectionStringParameter]
        [Description("Defines configured SNMP forwarding agent authorization phrase to use when forwarding is enabled. Must be at least 16 characters.")]
        [DefaultValue(DefaultForwardAuthPhrase)]
        public string ForwardAuthPhrase { get; set; } = DefaultForwardAuthPhrase;

        /// <summary>
        /// Gets or sets configured SNMP forwarding agent encryption key to use when forwarding is enabled. Must be at least 16 characters.
        /// </summary>
        [ConnectionStringParameter]
        [Description("Defines configured SNMP forwarding agent encryption key to use when forwarding is enabled. Must be at least 16 characters.")]
        [DefaultValue(DefaultForwardEncryptKey)]
        public string ForwardEncryptKey { get; set; } = DefaultForwardEncryptKey;

        /// <summary>
        /// Gets the flag indicating if this adapter supports temporal processing.
        /// </summary>
        public override bool SupportsTemporalProcessing => false;

        /// <summary>
        /// Returns the detailed status of the data input source.
        /// </summary>
        public override string Status
        {
            get
            {
                StringBuilder status = new StringBuilder();

                status.Append(base.Status);
                status.AppendFormat("   Configuration File Name: {0}", ConfigFileName);
                status.AppendLine();
                status.AppendFormat("      SNMP Proxy Host Port: {0:N0}", SnmpPort);
                status.AppendLine();
                status.AppendFormat(" Total SNMP Traps Received: {0:N0}", m_totalReceivedSnmpTraps);
                status.AppendLine();
                status.AppendFormat(" Total Database Operations: {0:N0}", m_totalDatabaseOperations);
                status.AppendLine();
                status.AppendFormat("  Last DB Operation Result: {0}", m_lastDatabaseOperationResult?.ToString() ?? "null");
                status.AppendLine();
                status.AppendFormat("  Forwarding Agent Enabled: {0}", ForwardingEnabled);
                status.AppendLine();

                if (ForwardingEnabled)
                {
                    status.AppendFormat(" Forwarded Agent Community: {0}", string.IsNullOrWhiteSpace(ForwardCommunity) ? "<set to forward original source value>" : ForwardCommunity);
                    status.AppendLine();
                    status.AppendFormat(" Forwarded Agent End Point: {0}", ForwardIPEndPoint);
                    status.AppendLine();
                }

                return status.ToString();
            }
        }

        #endregion

        #region [ Methods ]

        /// <summary>
        /// Initializes <see cref="SnmpHost" />.
        /// </summary>
        public override void Initialize()
        {
            Dictionary<string, string> settings = Settings;

            if (!settings.TryGetValue(nameof(FramesPerSecond), out _))
                settings[nameof(FramesPerSecond)] = DefaultFramesPerSecond.ToString();

            if (!settings.TryGetValue(nameof(LagTime), out _))
                settings[nameof(LagTime)] = DefaultLagTime.ToString();

            if (!settings.TryGetValue(nameof(LeadTime), out _))
                settings[nameof(LeadTime)] = DefaultLeadTime.ToString();

            base.Initialize();

            m_commandParameters = new ConcurrentQueue<string[]>();

            ConnectionStringParser parser = new ConnectionStringParser();
            parser.ParseConnectionString(ConnectionString, this);

            // Load configuration
            if (string.IsNullOrWhiteSpace(ConfigFileName))
                throw new InvalidOperationException("No configuration file name specified. Cannot initialize adapter.");

            ConfigFileName = FilePath.GetAbsolutePath(ConfigFileName);

            if (!File.Exists(ConfigFileName))
                throw new InvalidOperationException($"Configuration file name \"{ConfigFileName}\" not found. Cannot initialize adapter.");

            m_config = Config.Load(ConfigFileName);

            // Load optional database settings
            if (settings.TryGetValue(nameof(DatabaseConnnectionString), out string setting) && !string.IsNullOrWhiteSpace(setting))
                DatabaseConnnectionString = setting;
            else
                DatabaseConnnectionString = DefaultDatabaseConnectionString;

            if (settings.TryGetValue(nameof(DatabaseProviderString), out setting) && !string.IsNullOrWhiteSpace(setting))
                DatabaseProviderString = setting;
            else
                DatabaseProviderString = DefaultDatabaseProviderString;

            if (settings.TryGetValue(nameof(DatabaseCommand), out setting) && !string.IsNullOrWhiteSpace(setting))
                DatabaseCommand = setting;
            else
                DatabaseCommand = DefaultDatabaseCommand;

            if (settings.TryGetValue(nameof(DatabaseCommandTemplate), out setting) && !string.IsNullOrWhiteSpace(setting))
                DatabaseCommandTemplate = setting;
            else
                DatabaseCommandTemplate = DefaultDatabaseCommandTemplate;

            if (settings.TryGetValue(nameof(DatabaseMaximumWriteInterval), out setting) && double.TryParse(setting, out double interval))
                DatabaseMaximumWriteInterval = interval;
            else
                DatabaseMaximumWriteInterval = DefaultDatabaseMaximumWriteInterval;

            // Define synchronized monitoring operation
            m_databaseOperation = new DelayedSynchronizedOperation(DatabaseOperation, exception => OnProcessException(MessageLevel.Warning, exception))
            {
                Delay = (int)(DatabaseMaximumWriteInterval * 1000.0D)
            };

            if (ForwardingEnabled)
            {
                m_forwardCommunity = string.IsNullOrWhiteSpace(ForwardCommunity) ? null : new OctetString(ForwardCommunity);

                if (string.IsNullOrWhiteSpace(ForwardIPEndPoint))
                    throw new InvalidOperationException($"Configured SNMP forwarding agent IP end point \"{nameof(ForwardIPEndPoint)}\" must be defined when SNMP forwarding is enabled");

                string endPoint = ForwardIPEndPoint;
                string[] parts = endPoint.Split(':');

                if (parts.Length != 2)
                    throw new InvalidOperationException($"Configured SNMP forwarding agent IP end point \"{endPoint}\" format is invalid.");

                if (!IPAddress.TryParse(parts[0], out IPAddress address))
                    throw new InvalidOperationException($"Configured SNMP forwarding agent IP end point \"{endPoint}\" address is invalid");

                if (!ushort.TryParse(parts[1], out ushort port))
                    throw new InvalidOperationException($"Configured SNMP forwarding agent IP end point \"{endPoint}\" port is invalid");

                m_forwardIPEndPoint = new IPEndPoint(address, port);

                if (string.IsNullOrWhiteSpace(ForwardAuthPhrase))
                    throw new InvalidOperationException($"Configured SNMP forwarding agent authorization phrase \"{nameof(ForwardAuthPhrase)}\" must be defined when SNMP forwarding is enabled");

                if (string.IsNullOrWhiteSpace(ForwardEncryptKey))
                    throw new InvalidOperationException($"Configured SNMP forwarding agent encryption key \"{nameof(ForwardEncryptKey)}\" must be defined when SNMP forwarding is enabled");

                if (ForwardAuthPhrase.Length < 16)
                {
                    ForwardAuthPhrase = ForwardAuthPhrase.PadRight(16, '#');
                    OnStatusMessage(MessageLevel.Warning, $"Configured SNMP forwarding agent authorization phrase \"{nameof(ForwardAuthPhrase)}\" was too short - value right-padded with \"#\" characters. Destination SNMP target node authorization phrase will need to be adjusted accordingly.");
                }

                if (ForwardEncryptKey.Length < 16)
                {
                    ForwardEncryptKey = ForwardEncryptKey.PadRight(16, '#');
                    OnStatusMessage(MessageLevel.Warning, $"Configured SNMP forwarding agent encryption key \"{nameof(ForwardEncryptKey)}\" was too short - value right-padded with \"#\" characters. Destination SNMP target node encryption key will need to be adjusted accordingly.");
                }

                m_forwardPrivacyProvider = new DESPrivacyProvider(
                    new OctetString(ForwardEncryptKey),
                    new MD5AuthenticationProvider(new OctetString(ForwardAuthPhrase)));
            }

            if (SnmpPort < 1 || SnmpPort > ushort.MaxValue)
                throw new InvalidOperationException($"Configured SNMP proxy host port \"{SnmpPort}\" port is invalid. Valid possible range is from 1 to {ushort.MaxValue}");

            // Setup SNMP host engine
            UserRegistry users = new UserRegistry();

            foreach (Source source in m_config.Sources)
            {
                source.ParseMappings();

                users.Add
                (
                    new OctetString(source.Community),
                    new DESPrivacyProvider(
                        new OctetString(source.EncryptKey),
                        new MD5AuthenticationProvider(new OctetString(source.AuthPhrase)))
                    {
                        EngineIds = new List<OctetString> { Snmp.EngineID }
                    }
                );
            }

            TrapV2MessageHandler messageHandler = new TrapV2MessageHandler();
            messageHandler.MessageReceived += MessageHandler_MessageReceived;

            HandlerMapping snmpV3TrapMapping = new HandlerMapping("v3", "TRAPV2", messageHandler);
            ObjectStore store = new ObjectStore();
            Version3MembershipProvider membershipProvider = new Version3MembershipProvider();
            ComposedMembershipProvider membership = new ComposedMembershipProvider(new IMembershipProvider[] { membershipProvider });
            MessageHandlerFactory handlerFactory = new MessageHandlerFactory(new[] { snmpV3TrapMapping });
            SnmpApplicationFactory pipelineFactory = new SnmpApplicationFactory(store, membership, handlerFactory);

            m_snmpEngine = new SnmpEngine(pipelineFactory, new Listener { Users = users }, new EngineGroup());
            m_snmpEngine.Listener.AddBinding(new IPEndPoint(IPAddress.Any, SnmpPort));
            m_snmpEngine.Start();
        }

        private void MessageHandler_MessageReceived(object sender, TrapV2MessageReceivedEventArgs e)
        {
            TrapV2Message message = e.TrapV2Message;
            string community = message.Parameters.UserName.ToString();
            m_totalReceivedSnmpTraps++;

            if (m_config.CommunityMap.TryGetValue(message.Parameters.UserName.ToString(), out Source source))
            {
                StringBuilder output = new StringBuilder();
                IList<Variable> variables = message.Variables();

                output.AppendLine($"{message.Version} trap message from {message.Community()} [{message.Enterprise}] with {variables.Count:N0} variables");

                foreach (Variable variable in variables)
                    output.AppendLine($"    {variable}");

                OnStatusMessage(MessageLevel.Info, output.ToString());

                // Lookup any mapped variables
                foreach (Variable variable in variables)
                {
                    string value = variable.Data.ToString();

                    if (source.OIDMap.TryGetValue(variable.Id, out List<Mapping> mappings))
                    {
                        foreach (Mapping mapping in mappings)
                        {
                            object parsedValue = ParseValue(value);

                            // Provide value to mapping for condition evaluation
                            mapping.SetValue(parsedValue);

                            // Continue mapping operations when condition evaluates to true
                            if (!mapping.ConditionSuccessful)
                                continue;

                            TemplatedExpressionParser parameterTemplate = new TemplatedExpressionParser
                            {
                                TemplatedExpression = mapping.Description
                            };

                            string formattedValue;

                            switch (parsedValue)
                            {
                                case double dVal:
                                    formattedValue = $"{dVal:N3}";
                                    break;
                                case int iVal:
                                    formattedValue = $"{iVal:N0}";
                                    break;
                                case DateTime dtVal:
                                    formattedValue = dtVal.ToString(TimeTagBase.DefaultFormat);
                                    break;
                                default:
                                    formattedValue = parsedValue.ToString();
                                    break;
                            }

                            Dictionary<string, string> substitutions = new Dictionary<string, string>
                            {
                                ["{Value}"] = formattedValue,
                                ["{Timestamp}"] = RealTime.ToString(TimeTagBase.DefaultFormat)
                            };

                            string description = parameterTemplate.Execute(substitutions);

                            parameterTemplate = new TemplatedExpressionParser
                            {
                                TemplatedExpression = DatabaseCommandTemplate
                            };

                            substitutions = new Dictionary<string, string>
                            {
                                ["{EventType}"] = ((int)mapping.EventType).ToString(),
                                ["{Flow}"] = mapping.Flow,
                                ["{Description}"] = description
                            };

                            string[] commandParameters = parameterTemplate.Execute(substitutions).Split(',');
                            m_commandParameters.Enqueue(commandParameters);
                            m_databaseOperation?.RunOnceAsync();
                        }
                    }
                }

                // Forward SNMP messages if configured to do so
                if (ForwardingEnabled && source.Forward)
                {
                    Task.Run(async () =>
                    {
                        await new TrapV2Message
                        (
                            VersionCode.V3,
                            Messenger.NextMessageId,
                            Messenger.NextRequestId,
                            m_forwardCommunity ?? new OctetString(source.Community),
                            Snmp.EnterpriseRoot,
                            (uint)Environment.TickCount / 10,
                            variables,
                            m_forwardPrivacyProvider,
                            Messenger.MaxMessageSize,
                            Snmp.EngineID, 0, 0
                        )
                        .SendAsync(m_forwardIPEndPoint);
                    })
                    .ContinueWith(task =>
                    {
                        OnProcessException(MessageLevel.Error, new InvalidOperationException($"Failed to forward SNMP V3 trap to {m_forwardIPEndPoint}: {task.Exception?.Message}", task.Exception));
                    }, TaskContinuationOptions.OnlyOnFaulted);
                }
            }
            else
            {
                OnStatusMessage(MessageLevel.Warning, $"Failed to find configured source for community \"{community}\" in \"{ConfigFileName}\".");
            }
        }

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="SnmpHost"/> object and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected override void Dispose(bool disposing)
        {
            if (!m_disposed)
            {
                try
                {
                    // This will be done regardless of whether the object is finalized or disposed.

                    if (disposing)
                    {
                        m_snmpEngine?.Stop();
                        m_snmpEngine?.Dispose();
                    }
                }
                finally
                {
                    m_disposed = true;          // Prevent duplicate dispose.
                    base.Dispose(disposing);    // Call base class Dispose().
                }
            }
        }

        /// <summary>
        /// Gets a short one-line status of this <see cref="T:GSF.TimeSeries.Adapters.AdapterBase" />.
        /// </summary>
        /// <param name="maxLength">Maximum number of available characters for display.</param>
        /// <returns>A short one-line summary of the current status of this <see cref="T:GSF.TimeSeries.Adapters.AdapterBase" />.</returns>
        public override string GetShortStatus(int maxLength)
        {
            if (Enabled)
                return "Listening for SNMP values...".CenterText(maxLength);

            return "Not currently listening for SNMP values...".CenterText(maxLength);
        }

        /// <summary>
        /// Queues database operation for execution. Operation will execute immediately if not already running.
        /// </summary>
        [AdapterCommand("Executes any queued database operations for execution. Operation will execute immediately if not already running.", "Administrator", "Editor")]
        public void ExecuteOperation() => m_databaseOperation?.RunOnce();

        private void DatabaseOperation()
        {
            using (AdoDataConnection connection = string.IsNullOrWhiteSpace(DatabaseConnnectionString) ? new AdoDataConnection("systemSettings") : new AdoDataConnection(DatabaseConnnectionString, DatabaseProviderString))
            {
                while (m_commandParameters.TryDequeue(out string[] commandParameters))
                {
                    m_lastDatabaseOperationResult = connection.ExecuteScalar(DatabaseCommand, commandParameters.Select(commandParameter => ParseValue(commandParameter.Trim())).ToArray());
                    m_totalDatabaseOperations++;
                }
            }
        }

        private object ParseValue(string value)
        {
            // Do some basic typing on command parameters
            if (value.StartsWith("'") && value.EndsWith("'"))
                return value.Length > 2 ? value.Substring(1, value.Length - 2) : "";
            
            if (int.TryParse(value, out int ival))
                return ival;
            
            if (double.TryParse(value, out double dval))
                return dval;

            if (bool.TryParse(value, out bool bval))
                return bval;

            if (DateTime.TryParseExact(value, TimeTagBase.DefaultFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTime dtval))
                return dtval;
            
            if (DateTime.TryParse(value, out dtval))
                return dtval;

            return value;
        }

        #endregion
    }
}
