using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;

//Mogelijkheid maken om data te verwijderen
//Maximale data schrijven
//Maximale data lezen

namespace IFM_DTE804_console
{
    class Program
    {
        #region Declarations
        //Declarate variables
        private static TcpClient _client;
        private static NetworkStream _clientStream;
        private static char[] _delimiterChars = { '_', '\r', '\n' };
        private static List<dataEPC> _listEPC = new List<dataEPC>();
        private static List<string> _diagData = new List<string>();
        private static bool _errorActive;

        //Create class to safe the tag data
        private class dataEPC
        {
            public int lengthEPC { get; set; }
            public string tagEPC { get; set; }
            public int phaseTag { get; set; }
            public float rssiTag { get; set; }
            public int Index { get; set; }
        }

        //Check enummeration of the reader
        enum ReadEPCResult
        {
            Error,
            DiagnosticsAvailable,
            NoTagFound,
            TagFound
        }

        //Current status of the program
        enum Status
        {
            None,
            RefreshOnce,
            RefreshContinues,
            ReadMemory,
            WriteMemory,
            SpecificCommand,
            CloseApplication
        }
        #endregion

        static void Main()
        {
            //Create default IP, with possibility to connect with another IP.
            var ipAdress = "192.168.10.181";
            Console.WriteLine("Enter the IP adress to connect:");

            var suggestedIP = Console.ReadLine();
            if (!string.IsNullOrEmpty(suggestedIP))
                ipAdress = suggestedIP;

            Console.WriteLine($"Program started, try to connect to the DTE804 via IP: {ipAdress} ...");

            //Connect and configure DTE804
            while (!Connect(ipAdress))
            {
                Console.WriteLine("Try again with each key, exit with 'e'.");
                if (Console.ReadKey().Key == ConsoleKey.E)
                    ExitConsole();
            }

            //Configure controller after connection
            ConfigDTE();
            ConfigIOChannel();

            Console.WriteLine($"Connected");

            //Start program
            while (true)
                MainMenu();
        }

        #region Main menu
        private static void MainMenu()
        {
            //Get the EPC results
            var result = RefreshEPCData();
            ErrorActiveHandling();

            Console.Clear();

            //Set keyboard status on default
            Status keyboard = Status.None;

            switch (result)
            {
                //Check on diagnostics and show this
                case ReadEPCResult.DiagnosticsAvailable:
                    Console.WriteLine("Diagnostics available.");
                    DisplayDiagnostics();

                    Console.WriteLine("Any key to go back");
                    Console.ReadKey();
                    MainMenu();
                    break;

                //By no tag found, refresh unil tag is found
                case ReadEPCResult.NoTagFound:
                    Console.WriteLine("No tags found.");
                    while (_listEPC.Count() == 0)
                    {
                        RefreshEPCData();
                        Thread.Sleep(50);
                    }
                    MainMenu();
                    break;

                //Create menu if tags are found.
                case ReadEPCResult.TagFound:
                    //Show all tag information
                    Console.WriteLine("Tags found:");
                    Console.WriteLine("Index\tLength\tRSSI\tEPC");
                    foreach (var EPC in _listEPC)
                        Console.WriteLine($"{EPC.Index}:\t{EPC.lengthEPC}\t{EPC.rssiTag}\t{EPC.tagEPC}");
                    Console.WriteLine();

                    //Get options
                    Console.WriteLine("What do you want to do?");
                    Console.WriteLine();
                    Console.WriteLine("1:\tRefresh EPCs (once)");
                    Console.WriteLine("2:\tRefresh EPCs (continues)");
                    Console.WriteLine("3:\tRead user memory");
                    Console.WriteLine("4:\tWrite user memory");
                    Console.WriteLine("5:\tSpecific command");
                    Console.WriteLine();
                    Console.WriteLine("e:\tExit");
                    Console.WriteLine();
                    Console.Write("Your choice: ");

                    //Save options on keyboard status
                    switch (Console.ReadKey().Key)
                    {
                        case ConsoleKey.NumPad1:
                        case ConsoleKey.D1:
                            keyboard = Status.RefreshOnce;
                            break;

                        case ConsoleKey.NumPad2:
                        case ConsoleKey.D2:
                            keyboard = Status.RefreshContinues;
                            break;

                        case ConsoleKey.NumPad3:
                        case ConsoleKey.D3:
                            keyboard = Status.ReadMemory;
                            break;

                        case ConsoleKey.NumPad4:
                        case ConsoleKey.D4:
                            keyboard = Status.WriteMemory;
                            break;

                        case ConsoleKey.NumPad5:
                        case ConsoleKey.D5:
                            keyboard = Status.SpecificCommand;
                            break;

                        case ConsoleKey.E:
                            keyboard = Status.CloseApplication;
                            break;
                    }
                    break;

            }

            switch (keyboard)
            {
                //Refresh once by this option
                case Status.RefreshOnce:
                    keyboard = Status.None;
                    MainMenu();
                    break;

                //Refresh continues with no options
                case Status.RefreshContinues:
                    Console.Clear();

                    Console.WriteLine("Tags found:");
                    Console.WriteLine("Index\tLength\tRSSI\tEPC");
                    foreach (var item in _listEPC)
                        Console.WriteLine($"{item.Index}:\t{item.lengthEPC}\t{item.rssiTag}\t{item.tagEPC}");

                    Console.WriteLine();
                    Console.WriteLine("Press any key to continue");

                    while (!Console.KeyAvailable)
                    {
                        var oldList = _listEPC.Select(i => new { i.tagEPC, i.rssiTag }).ToArray();
                        RefreshEPCData();
                        var newList = _listEPC.Select(i => new { i.tagEPC, i.rssiTag }).ToArray();

                        if (!oldList.SequenceEqual(newList))
                        {
                            Console.Clear();
                            Console.WriteLine("Tags found:");
                            Console.WriteLine("Index\tLength\tRSSI\tEPC");
                            foreach (var item in _listEPC)
                                Console.WriteLine($"{item.Index}:\t{item.lengthEPC}\t{item.rssiTag}\t{item.tagEPC}");

                            Console.WriteLine();
                            Console.WriteLine("Press any key to continue");
                        }

                        Thread.Sleep(100);
                    }
                    break;

                //Read user memory of a tag
                case Status.ReadMemory:
                    keyboard = Status.None;
                    //menu write EPC
                    Console.WriteLine("Read memory.");
                    Console.WriteLine("Which EPC Index do you want to read, confirm with enter: ");
                    String tagName = Console.ReadLine();
                    if (!_listEPC.Any(i => i.Index.ToString() == tagName.ToString()))
                    {
                        Console.WriteLine("EPC with that index not found. Please try again.");
                        Thread.Sleep(500);
                        MainMenu();
                    }

                    //Check of tag still excists
                    var EPC = _listEPC.First(i => i.Index.ToString() == tagName.ToString());
                    RefreshEPCData();
                    if (_listEPC.Any(i => i.tagEPC == EPC.tagEPC))
                    {
                        //send command read epc
                        var memory = ReadUserMemoryEPC(EPC);
                        ErrorActiveHandling();

                        Console.WriteLine();
                        Console.WriteLine($"User Memory: {memory}");
                    }
                    else
                        Console.WriteLine($"Tag {EPC.tagEPC} disconnected. Please try again.");

                    Console.WriteLine();
                    Console.WriteLine("Any key to go back");
                    Console.ReadKey();
                    MainMenu();
                    break;

                //Write user memory on a tag
                case Status.WriteMemory:
                    keyboard = Status.None;
                    //menu write EPC
                    Console.WriteLine("Write memory");
                    Console.WriteLine("Which EPC Index do you want to write, confirm with enter: ");
                    tagName = Console.ReadLine();
                    if (!_listEPC.Any(i => i.Index.ToString() == tagName.ToString()))
                    {
                        Console.WriteLine("EPC with that index not found. Please try again.");
                        Thread.Sleep(500);
                        MainMenu();
                    }

                    //Check of tag still excists
                    EPC = _listEPC.First(i => i.Index.ToString() == tagName.ToString());
                    RefreshEPCData();
                    if (_listEPC.Any(i => i.tagEPC == EPC.tagEPC))
                    {
                        Console.WriteLine("Which text do you want to write, confirm with enter: ");
                        string text = Console.ReadLine();
                        //send command write epc
                        WriteUserMemoryEPC(EPC, text);
                    }
                    else
                        Console.WriteLine($"Tag {EPC.tagEPC} disconnected. Please try again.");

                    Console.WriteLine();
                    Console.WriteLine("Any key to go back");
                    Console.ReadKey();
                    MainMenu();
                    break;

                //Get a specific option
                case Status.SpecificCommand:
                    keyboard = Status.None;
                    Console.WriteLine("Specific command");
                    Console.WriteLine("Which command do you want to write, confirm with enter: ");
                    string command = Console.ReadLine();
                    string feedback = SendMessage(command);
                    Console.WriteLine($"Feedback from command: {feedback}");
                    Console.WriteLine();
                    Console.WriteLine("Any key to go back");
                    Console.ReadKey();
                    MainMenu();
                    break;

                //Close application
                case Status.CloseApplication:
                    keyboard = Status.None;
                    Disconnect();
                    ExitConsole();
                    break;
            }

        }
        #endregion

        #region Connect and configure

        //Connect to DTE804 with timeout of 1 second.
        private static bool Connect(string IpAdress)
        {
            //Open TCP connection with controller
            Console.WriteLine("Connection started");
            _client = new TcpClient();

            //connection data DTE804
            var result = _client.BeginConnect(IpAdress, 33000, null, null);
            var connected = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(1));
            if (connected)
                Console.WriteLine("DTE804 connected.");
            else
                Console.WriteLine("Connection with DTE804 failed.");

            if (connected)
                _client.EndConnect(result);

            Console.WriteLine();
            return connected;
        }

        //Configure DTE
        private static void ConfigDTE() => SendMessage("CU_00_00_00_00_00_AS\r\n");

        //Configure IO
        private static void ConfigIOChannel() => SendMessage("CI_01_11_0000_002_000_00_00_00\r\n");
        #endregion

        #region Global control
        //Send default message
        private static string SendMessage(string msg)
        {
            //NetworkStream 
            _clientStream = _client.GetStream();

            ASCIIEncoding encoder = new ASCIIEncoding();
            byte[] buffer = encoder.GetBytes(msg);

            _clientStream.Write(buffer, 0, buffer.Length);
            _clientStream.Flush();

            // Receive the TcpServer response. 

            // Buffer to store the response bytes. 
            Byte[] data = new Byte[256];

            // String to store the response ASCII representation. 
            String responseData = String.Empty;

            Int32 bytes;

            // Read the first batch of the TcpServer response bytes. 
            bytes = _clientStream.Read(data, 0, data.Length);
            responseData = System.Text.Encoding.ASCII.GetString(data, 0, bytes);

            //write response data to text box
            return responseData;
        }

        //Refresh EPC Data and save it in a global list.
        private static ReadEPCResult RefreshEPCData()
        {
            _listEPC.Clear();

            //send command via TCP/IP
            String rawAnswer = SendMessage("RP_01\r\n");

            //split answer by seperator "_"
            String[] split_data = rawAnswer.Split(_delimiterChars);

            if (split_data.Count() < 4)
            {
                Console.WriteLine("Not possible to split the tag data in 3 parts!");
                _errorActive = true;
                return ReadEPCResult.Error;
            }

            //parse parameters from answer string
            String command = split_data[0];
            String port = split_data[1];
            String diagnosis = split_data[2];

            //check if diagnosis bit is true
            if (diagnosis != "00")
                return ReadEPCResult.DiagnosticsAvailable;

            //if diag == 0 get info tag count
            int tagCount = int.Parse(split_data[3]);

            //check if tags in field
            if (tagCount == 0)
                return ReadEPCResult.NoTagFound;

            for (int i = 0; i < tagCount; i++)
            {
                var newTagFound = new dataEPC();
                string rssiTag = split_data[4 + (i * 4)];
                newTagFound.rssiTag = float.Parse(rssiTag.Replace(".", ","));
                newTagFound.phaseTag = int.Parse(split_data[5 + (i * 4)]);
                newTagFound.lengthEPC = int.Parse(split_data[6 + (i * 4)]);
                newTagFound.tagEPC = split_data[7 + (i * 4)];
                _listEPC.Add(newTagFound);
            }

            _listEPC = _listEPC.OrderByDescending(item => item.rssiTag).ToList();
            int index = 1;
            foreach (var EPC in _listEPC)
            {
                EPC.Index = index;
                index++;
            }

            return ReadEPCResult.TagFound;
        }

        //By program error active, close application.
        private static void ErrorActiveHandling()
        {
            if (_errorActive)
            {
                Console.WriteLine("Error found! Close application with any key.");
                Console.ReadKey();
                ExitConsole();
            }
        }

        //Disconnect TCP client;
        private static void Disconnect()
        {
            //Close connection to DTE
            _client.Close();
            _client = null;
            _clientStream = null;
        }

        private static void ExitConsole()
        {
            Console.Clear();
            Environment.Exit(0);
        }
        #endregion

        #region Options
        //Display diagnostics
        private static void DisplayDiagnostics()
        {
            var result = ReadDiagnostics();
            Console.WriteLine($"Result: {result}");
            Console.WriteLine();
            foreach (var diag in _diagData)
            {
                Console.WriteLine($"Diganostic: {diag}");
                if (_errorCodes.TryGetValue(diag, out string diagResult))
                    Console.WriteLine(diagResult);
                else
                    Console.WriteLine("Unknow error.");
            }

            Console.WriteLine();
        }

        //Get diagnostics from DTE804
        private static string ReadDiagnostics()
        {
            _diagData.Clear();

            //send command via TCP/IP
            String rawAnswer = SendMessage("DI_01\r\n");
            //split answer by seperator "_"
            String[] split_data = rawAnswer.Split(_delimiterChars);

            if (split_data.Count() < 4)
            {
                Console.WriteLine("Not possible to split the tag data in 3 parts!");
                _errorActive = true;
                return String.Empty;
            }

            //parse parameters from answer string
            String command = split_data[0];
            String port = split_data[1];
            String diagnosis = split_data[2];

            //check if diagnosis bit is true
            if (diagnosis != "00")
                return "DIAGNOSIS";

            String countDiagnosis = split_data[3];

            //check if diagnosis is available
            if (countDiagnosis == "00")
                return "NO_DIAGNOSIS";

            //fill diag array with messages
            for (int i = 0; i < (int.Parse(countDiagnosis)); i++)
                _diagData.Add(split_data[4 + i]);

            return "DIAGNOSIS_DATA";
        }

        //Read user memory of the tag
        private static string ReadUserMemoryEPC(dataEPC dataEPC)
        {
            string hexLength = dataEPC.lengthEPC.ToString("00");
            //send command via TCP/IP
            String rawAnswer = SendMessage($"RF_01_USR_{hexLength}_{dataEPC.tagEPC}_ASC_00000_0035\r\n");
            //split answer by seperator "_"
            String[] split_data = rawAnswer.Split(_delimiterChars);
            if (split_data.Count() < 10)
            {
                _errorActive = true;
                return string.Empty;
            }

            return split_data[9];
        }

        //Write user memory of the tag
        private static void WriteUserMemoryEPC(dataEPC dataEPCs, string Text)
        {
            string hexEPCLength = dataEPCs.lengthEPC.ToString("00");
            string hexTextlength = Text.Length.ToString("0000");
            //send command via TCP/IP
            String rawAnswer = SendMessage($"WF_01_USR_{hexEPCLength}_{dataEPCs.tagEPC}_ASC_00000_{hexTextlength}_{Text.ToString()}\r\n");
        }
        #endregion

        #region ErrorCodes
        private static Dictionary<string, string> _errorCodes = new Dictionary<string, string>()
        {
            { "F1FE0200", "ID tag presence error or R/W head communication error with the ID tag"},
            { "F1FE0300", "Address or command does not fit the ID tag characteristics, memory size invalid"},
            { "F1FE0400", "ID tag is defective, replace ID tag or battery"},
            { "F1FE0500", "ID tag memory overflow. EPC > 16 bytes "},
            { "F1FE0900", "Command not supported by the ID tag"},
            { "F1FE0A00", "Access violation e.g. block locked. Refer to ISO18000-x"},
            { "F1FE0B00", "General ID tag error which is not specified in detail"},
            { "F1FE0C00", "Unknown internal error"},

            { "F4FE0100", "Power supply failure"},
            { "F4FE0200", "Hardware failure, short circuit and overload"},
            { "F4FE0201", "Allowed temperature exceeded"},
            { "F4FE0300", "Read-/Write head not operating cause time out occurred"},
            { "F4FE0400", "Command buffer overflow IO-Server Queue (Internal error)"},
            { "F4FE0500", "Data buffer overflow, memory allocation (Internal error)"},
            { "F4FE0600", "Command in this mode not supported (Internal error)"},
            { "F4FE8100", "ID-Link Master inactive. e.g. after power (Internal error)"},
            { "F4FE8200", "Internal IO-Port server error (Internal error)"},
            { "F4FE8300", "IO-Port invalid parameter Internal error, e.g. channel (Internal error)"},
            { "F4FE8400", "Vendor specific error on PUT"},
            { "F4FE8500", "IO-Port server reset channel"},
            { "F4FE8600", "Data not available for delayed C/Q inputs or delayed EPC (Internal error)"},
            { "F4FE8700", "IO-Port channel reconfiguration not allowed yet (internal error)"},
            { "F4FE8800", "IO-Port parameter selector flag not set (internal error)"},
            { "F4FE8900", "General error detected from ID-Link Master "},
            { "F4FE8A00", "CRC error detected from ID-Link Master"},
            { "F4FE8B00", "Object not found detected from ID-Link Master"},
            { "F4FE8C00", "Data Read-/Write size within command not valid"},
            { "F4FE8D00", "IO-Port channel is reconfigured"},
            { "F4FE8E00", "Read-/Write head could not process command e.g. Read-/Write length exceeded, ID tag memory error, write to locked block"},
            { "F4FE8F00", "ID tag data length exceed (Block size * Block number)"},

            { "F4FE9001", "Short circuit at output driver detected"},
            { "F4FE9002", "Under voltage at output driver detected"},
            { "F4FE9003", "Overload at output driver detected"},
            { "F4FE9004", "Over temperature at output driver detected"},
            { "F4FE9005", "Line break to Read-/Write head"},
            { "F4FE9006", "Upper limit reached at output driver"},
            { "F4FE9007", "Under voltage at C/Qo detected"},
            { "F4FE9008", "Read-/Write head failure detected"},
            { "F4FE9009", "Read-/Write head communication error"},
            { "F4FE900A", "I²C communication error (Internal error)"},
            { "F4FE900B", "I²C communication parity error (Internal error)"},
            { "F4FE900C", "Command rejected cause antenna field switched off"},
            { "F4FE900D", "Internal data of PROFNET stack corrupt (Internal error)"},
            { "F4FE900E", "R/W head do not support this object"},
            { "F4FE9401", "Frontend Error detected by Read-/Write head"},
            { "F4FE9402", "General error detected by Read-/Write head"},
            { "F4FE9403", "ID-Link Error detected by Read-/Write head"},
            { "F4FE9404", "Buffer overrun Error detected by Read-/Write head"},
            { "F4FE9405", "Over temperature at front end detected"},
            { "F4FE9406", "R/W head error detect reverse power to high"},
            { "F4FE9407", "Reset of R/W head detected"},
            { "F4FE9408", "R/W head HAL error detected"},
            { "F4FEA000", "Invalid command code detected"},
            { "F4FEA001", "Invalid command parameter detected"},
            { "F4FEA002", "Invalid command data detected"},
            { "F4FEA003", "Ticket number or ticket length detected"},

            { "F4FEA100", "Configuration of device failed (CR1 / CR2)"},
            { "F4FEA200", "Configuration of IO-channel failed (Internal error)"},
            { "F4FEA300", "Reading of Inputs C/Qi / IQ (Internal error)"},
            { "F4FEA400", "Write of output C/Qo failed (Internal error)"},
            { "F4FEA500", "Setting of high current failed (Internal error)"},
            { "F4FEA600", "Read of EPC failed (Internal error)"},
            { "F4FEA700", "Read of User data memory of the ID tag failed (Internal error)"},
            { "F4FEA800", "Write to user memory of the ID tag failed, command WU (Internal error)"},
            { "F4FEA900", "Write to user memory of the ID tag failed, command WV (Internal error)"},
            { "F4FEAA00", "Verification of the user memory of the ID tag failed, commands “WV” (Internal error)"},
            { "F4FEAB00", "Setting of Antenna field on/off failed, command “AN”"},
            { "F4FEAC00", "ID-Link master could not read the ID tag blocks (Internal error)"},
            { "F4FEAD00", "Block size/number of blocks could not be read from the ID tag"},
            { "F4FED100", "Internal command execution failure"},
            { "F4FEFF00", "Internal command execution failure"},

            { "F5FE0800", "Command from another user being processed (indicated by device)"},
            { "F5FE8000", "More than one command requested by User (DR,WR,Diag)"},
            { "F5FE8100", "Synchronous read or write command is tried to abort"},
            { "F5FE8300", "Asynchronous read command parameter invalid"},
            { "F5FE8400", "Invalid command request in module RWH_CMD detected"},
            { "F5FE8500", "Module size to short to execute commands"},
            { "F6FE0300", "Invalid command parameter (e.g. data range) (indicated by device)"}
        };
        #endregion
    }
}

