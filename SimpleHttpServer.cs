using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

// point any web-browser to http://localhost:8080 to see the HTML pages rendered

/*
  This simple webserver is broken into two components
  
  The HttpServer class opens a TcpListener on the incoming port, 
  and sits in a loop handling incoming TCP connect requests using AcceptTcpClient(). 
  This is the first step of handling an incoming TCP connection. 
 
  The incoming request arrived on our "well known port", 
  and this accept process creates a fresh port-pair for server to communicate with this client on. 
  That fresh port-pair is our TcpClient session. 
  This keeps our main accept port free to accept new connections. 
  
  Each time the listener returns a new TcpClient, 
  HttpServer creates a new HttpProcessor and starts a new thread for it to operate in. 
  This class also contains the abstract methods our subclass must implement in order to produce a response.
*/


namespace SimpleServerAssignment {

    public class HttpProcessor {
        public TcpClient socket;        
        public HttpServer httpServer;

        private Stream inputStream;
        public  StreamWriter outputStream;

        public String http_method;
        public String http_url;

        public Hashtable httpHeaders = new Hashtable();

        private static int MAX_POST_SIZE = 10 * 1024 * 1024; // 10MB

        public HttpProcessor(TcpClient tcpClient, HttpServer httpServer) {
            this.socket = tcpClient;
            this.httpServer = httpServer;                   
        }
        

        private string streamReadLine(Stream inputStream) {
            int next_char;
            string data = "";
            while (true) {
                next_char = inputStream.ReadByte();
                if (next_char == '\n') { break; }
                if (next_char == '\r') { continue; }
                if (next_char == -1) { Thread.Sleep(1); continue; };
                data += Convert.ToChar(next_char);
            }            
            return data;
        }
        public void process() {                        
            inputStream = new BufferedStream(socket.GetStream());

            outputStream = new StreamWriter(new BufferedStream(socket.GetStream()));
            try {
                parseRequest();
                readHeaders();
                if (http_method.Equals("GET")) {
                    handleGETRequest();
                } else if (http_method.Equals("POST")) {
                    handlePOSTRequest();
                }
            } catch (Exception e) {
                Console.WriteLine("Exception: " + e.ToString());
                writeFailure();
            }
            outputStream.Flush();  // flush any remaining output
            inputStream = null; outputStream = null;             
            socket.Close();             
        }

        public void parseRequest() {
            String request = streamReadLine(inputStream);
            string[] tokens = request.Split(' ');
            if (tokens.Length != 3) {
                throw new Exception("invalid http request line");
            }
            http_method = tokens[0].ToUpper();
            http_url = tokens[1];

            Console.WriteLine("starting: " + request);
        }

        public void readHeaders() {
            Console.WriteLine("readHeaders()");
            String line;
            while ((line = streamReadLine(inputStream)) != null) {
                if (line.Equals("")) {
                    Console.WriteLine("got headers");
                    return;
                }
                
                int separator = line.IndexOf(':');
                if (separator == -1) {
                    throw new Exception("invalid http header line: " + line);
                }
                String name = line.Substring(0, separator);
                int pos = separator + 1;
                while ((pos < line.Length) && (line[pos] == ' ')) {
                    pos++; // strip any spaces
                }
                    
                string value = line.Substring(pos, line.Length - pos);
                Console.WriteLine("header: {0}:{1}",name,value);
                httpHeaders[name] = value;
            }
        }

        public void handleGETRequest() {
            httpServer.handleGETRequest(this);
        }

        private const int BUF_SIZE = 4096;

        // read all the input data into a MemoryStream before sending to the POST handler
        public void handlePOSTRequest() {
            Console.WriteLine("get post data start");
            int content_len = 0;
            MemoryStream ms = new MemoryStream();
            if (this.httpHeaders.ContainsKey("Content-Length")) {
                 content_len = Convert.ToInt32(this.httpHeaders["Content-Length"]);
                 if (content_len > MAX_POST_SIZE) {
                     throw new Exception(
                         String.Format("POST Content-Length({0}) too big for this server",
                           content_len));
                 }
                 byte[] buf = new byte[BUF_SIZE];              
                 int to_read = content_len;
                 while (to_read > 0) {  
                     Console.WriteLine("starting Read, to_read={0}",to_read);

                     int numread = this.inputStream.Read(buf, 0, Math.Min(BUF_SIZE, to_read));
                     Console.WriteLine("read finished, numread={0}", numread);
                     if (numread == 0) {
                         if (to_read == 0) {
                             break;
                         } else {
                             throw new Exception("client disconnected during post");
                         }
                     }
                     to_read -= numread;
                     ms.Write(buf, 0, numread);
                 }
                 ms.Seek(0, SeekOrigin.Begin);
            }
            Console.WriteLine("get post data end");
            httpServer.handlePOSTRequest(this, new StreamReader(ms));

        }
        public void writeSuccess(string content_type="text/html") {
            outputStream.WriteLine("HTTP/1.0 200 OK");            
            outputStream.WriteLine("Content-Type: " + content_type);
            outputStream.WriteLine("Connection: close");
            outputStream.WriteLine("");
        }
        public void writeFailure() {
            outputStream.WriteLine("HTTP/1.0 404 File not found");
            outputStream.WriteLine("Connection: close");
            outputStream.WriteLine("");
        }
    }

    // END OF PROCESSOR


    //
    //  abstract HttpServer provides listen() for connections on the incoming port, 
    //  instantiating the processor by starting a thread for the main server listener.
    //  Two abstract method declarations will handle Get and Post requests in the named class MyHttpServer. 
    //
    public abstract class HttpServer {

        protected int port;
        TcpListener listener;
        bool is_active = true;
       
        public HttpServer(int port) {
            this.port = port;
        }

        public void listen() {

            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            while (is_active) {                
                TcpClient tcpClient = listener.AcceptTcpClient();
                
                HttpProcessor processor = new HttpProcessor(tcpClient, this);
                // At this point, the new client-server TCP connection is handed off to the HttpProcessor 
                // in its own thread. The HttpProcessor's job is to properly parse the HTTP headers, 
                //and hand control to the proper abstract method handler implementation
                
                Thread thread = new Thread(new ThreadStart(processor.process));
                // process method gets input/output streams upon thread.Start()
                thread.Start();

                Thread.Sleep(1);
            }
        }

        public abstract void handleGETRequest(HttpProcessor p);
        public abstract void handlePOSTRequest(HttpProcessor p, StreamReader inputData);
    }

    // implement get and post handlers
    public class MyHttpServer : HttpServer {
        public MyHttpServer(int port) : base(port) {
        }
        public override void handleGETRequest (HttpProcessor hp)
		{
            Console.WriteLine("request: {0}", hp.http_url);
            hp.writeSuccess();
            hp.outputStream.WriteLine("<html><body><h1>Simple Server</h1>");
            hp.outputStream.WriteLine("Current Time: " + DateTime.Now.ToString());
            hp.outputStream.WriteLine("url : {0}", hp.http_url);

            hp.outputStream.WriteLine("<form method=post action=/form>");
            hp.outputStream.WriteLine("<input type=text name=FirstName value=FirstName>");
            hp.outputStream.WriteLine("<input type=submit name=ClickValue value=Click>");
            hp.outputStream.WriteLine("</form>");
        }


        public override void handlePOSTRequest(HttpProcessor hp, StreamReader inputData) {
            Console.WriteLine("POST request: {0}", hp.http_url);
            string data = inputData.ReadToEnd();

            hp.writeSuccess();
            hp.outputStream.WriteLine("<html><body><h1>Simple Server</h1>");
            hp.outputStream.WriteLine("<a href=/test>return</a><p>");
            hp.outputStream.WriteLine("postbody: <pre>{0}</pre>", data);
        }
    }

    public class StartMain {
        public static int Main(String[] args) {

            HttpServer httpServer;

            if (args.GetLength(0) > 0)
            {
                httpServer = new MyHttpServer(Convert.ToInt16(args[0]));
            }
            else
            {
                httpServer = new MyHttpServer(8080);  // default port
            }

            Thread thread = new Thread(new ThreadStart(httpServer.listen));
            // new ThreadStart delegate represents the listen method which
            // will be invoked when this thread starts
            thread.Start();
            return 0;
        }

    }

}



