using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Net.Http;
using System.Web.Script.Serialization;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
namespace TonyMods
{
    internal sealed partial class BrowserHost
    {
        private readonly WebView2 resolver = new WebView2();
        private readonly ListView queueView = new ListView(), historyView = new ListView();
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength=120000, RecursionLimit=16 };
        private JukeboxState queueState = new JukeboxState();
        private string resolveSpec;
        private long draggedItem;
        private readonly HttpClient titleClient = new HttpClient { Timeout=TimeSpan.FromSeconds(8), MaxResponseContentBufferSize=32768 };
        private readonly Dictionary<string,string> titles = new Dictionary<string,string>();
        private readonly Queue<string> titleJobs = new Queue<string>();
        private int titleWorkers;
        private void BuildQueuePanel(Control parent)
        {
            var layout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,66));
            var tabs=new TabControl {Dock=DockStyle.Fill};
            foreach(var pair in new[]{new KeyValuePair<string,ListView>("Up next (drag to reorder)",queueView),new KeyValuePair<string,ListView>("History",historyView)})
            {
                var page=new TabPage(pair.Key); var view=pair.Value;
                view.Dock=DockStyle.Fill;view.View=View.Details;view.FullRowSelect=true;view.MultiSelect=false;view.HideSelection=false;
                view.Columns.Add("#",35);view.Columns.Add("Song",200);view.Columns.Add("Player",75);
                page.Controls.Add(view);tabs.TabPages.Add(page);
            }
            queueView.AllowDrop=true;
            queueView.ItemDrag+=delegate(object s,ItemDragEventArgs e) {draggedItem=(long)((ListViewItem)e.Item).Tag;queueView.DoDragDrop(draggedItem.ToString(),DragDropEffects.Move);};
            queueView.DragOver+=delegate(object s,DragEventArgs e) {e.Effect=draggedItem>0 && e.Data.GetDataPresent(DataFormats.Text)?DragDropEffects.Move:DragDropEffects.None;};
            queueView.DragDrop+=delegate(object s,DragEventArgs e)
            {
                Point point=queueView.PointToClient(new Point(e.X,e.Y));var hit=queueView.GetItemAt(point.X,point.Y);
                if(draggedItem>0)QueueRequest("move",draggedItem,hit==null?0:(long)hit.Tag);draggedItem=0;
            };
            queueView.DoubleClick+=delegate {SelectedRequest("playitem");};
            var buttons=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=true};
            AddButton(buttons,"Up",55,delegate {MoveSelected(-1);});
            AddButton(buttons,"Down",60,delegate {MoveSelected(1);});
            AddButton(buttons,"Remove",75,delegate {SelectedRequest("remove");});
            AddButton(buttons,"Play selected",100,delegate {SelectedRequest("playitem");});
            AddButton(buttons,"Clear pending",115,delegate {QueueRequest("clear");});
            layout.Controls.Add(tabs,0,0);layout.Controls.Add(buttons,0,1);
            resolver.Dock=DockStyle.Top;resolver.Height=210;resolver.Visible=false;
            parent.Controls.Add(layout);parent.Controls.Add(resolver);
        }
        private static void AddButton(Control parent,string text,int width,Action action)
        {var button=new Button {Text=text,Width=width};button.Click+=delegate {action();};parent.Controls.Add(button);}
        private void SelectedRequest(string op)
        {if(queueView.SelectedItems.Count>0)QueueRequest(op,(long)queueView.SelectedItems[0].Tag);}
        private void MoveSelected(int direction)
        {
            if(queueView.SelectedItems.Count==0)return;
            var row=queueView.SelectedItems[0];int i=row.Index;
            if(direction<0 && i>0)QueueRequest("move",(long)row.Tag,(long)queueView.Items[i-1].Tag);
            if(direction>0 && i<queueView.Items.Count-1)QueueRequest("move",(long)row.Tag,i+2<queueView.Items.Count?(long)queueView.Items[i+2].Tag:0);
        }
        private void QueueRequest(string op,long item=0,long before=0,double position=0)
        {SendRequest(new JukeboxRequest {op=op,item=item,before=before,position=position,track=queueState.track,playRevision=queueState.playRevision});}
        private void TestQueueControls()
        {
            JukeboxState sample;string error;
            if(!queueState.TryInsert("append",new[]{"pEdxU1F-FE8","cfS4YBuKgEw","M7lc1UVf-VE"},1,42,0,out sample,out error))throw new Exception(error);
            queueState=sample;RenderQueue();
            if(queueView.Items.Count!=2 || historyView.Items.Count!=0 || queueView.Items[0].SubItems[1].Text!="cfS4YBuKgEw")throw new Exception("Queue rendering failed");
            queueView.Items[1].Selected=true;
            var output=Console.Out;var capture=new System.IO.StringWriter();
            try {Console.SetOut(capture);MoveSelected(-1);} finally {Console.SetOut(output);}
            string line=capture.ToString().Trim();
            if(!line.StartsWith("REQUEST "))throw new Exception("Queue action not emitted");
            var request=json.Deserialize<JukeboxRequest>(Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(8))));
            if(request.op!="move" || request.item!=sample.pending[1].id || request.before!=sample.pending[0].id || request.track!=sample.track)throw new Exception("Queue action selected wrong IDs");
            titles["M7lc1UVf-VE"]="Loaded title";RenderQueue();
            if(queueView.SelectedItems.Count!=1 || (long)queueView.SelectedItems[0].Tag!=sample.pending[1].id)throw new Exception("Metadata refresh lost selection");
            queueState=new JukeboxState();titles.Clear();RenderQueue();
            Console.WriteLine("QUEUE_UI_TEST=rows,ID-fallback,move-request,selection-preserved:PASS");
        }
        private void SendRequest(JukeboxRequest request)
        {Console.WriteLine("REQUEST "+Convert.ToBase64String(Encoding.UTF8.GetBytes(json.Serialize(request))));}
        private void ReadQueue(string encoded)
        {
            try
            {
                if(encoded.Length>160000)return;
                var next=json.Deserialize<JukeboxState>(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
                if(next==null || !next.Valid() || next.revision<queueState.revision)return;
                queueState=next;RenderQueue();
                if(next.current!=null)EnqueueTitle(next.current.video);
                foreach(var entry in next.pending)EnqueueTitle(entry.video);
                while(titleWorkers<2 && titleJobs.Count>0)FetchTitles();
            }
            catch(Exception ex){Console.WriteLine("QUEUE_ERROR "+ex.Message);}
        }
        private string EntryTitle(JukeboxEntry entry)
        {string title;return titles.TryGetValue(entry.video,out title) && !String.IsNullOrEmpty(title)?title:entry.title!=""?entry.title:entry.video;}
        private void RenderQueue()
        {
            RenderEntries(queueView,queueState.pending);RenderEntries(historyView,queueState.history);
            playlistStatus.Text=(queueState.current==null?"No current song":(queueState.stopped?"Stopped: ":queueState.paused?"Paused: ":"Now: ")+EntryTitle(queueState.current))+
                " | Pending "+queueState.pending.Length+"/200 | Repeat: "+new[]{"off","one","all"}[queueState.repeat]+"\r\n"+queueState.notice;
        }
        private void RenderEntries(ListView view,JukeboxEntry[] entries)
        {
            long selected=view.SelectedItems.Count>0?(long)view.SelectedItems[0].Tag:0;
            int top=view.TopItem==null?0:view.TopItem.Index;
            view.BeginUpdate();view.Items.Clear();
            for(int i=0;i<entries.Length;i++)
            {
                var entry=entries[i];var row=new ListViewItem(new[]{(i+1).ToString(),EntryTitle(entry),entry.addedBy==0?"Host":"Player "+entry.addedBy});row.Tag=entry.id;
                view.Items.Add(row);if(entry.id==selected)row.Selected=true;
            }
            view.EndUpdate();if(view.Items.Count>0)view.TopItem=view.Items[Math.Min(top,view.Items.Count-1)];
        }
        private void EnqueueTitle(string id)
        {
            if(titles.ContainsKey(id))return;
            // Keep a bounded cache. Failed metadata requests retain the video ID as a usable fallback.
            if(titles.Count>=512)return;
            titles[id]="";titleJobs.Enqueue(id);
        }
        private async void FetchTitles()
        {
            titleWorkers++;
            try
            {
                while(titleJobs.Count>0 && !IsDisposed)
                {
                    string id=titleJobs.Dequeue();
                    try
                    {
                        string response=await titleClient.GetStringAsync("https://www.youtube.com/oembed?url=https%3A%2F%2Fwww.youtube.com%2Fwatch%3Fv%3D"+id+"&format=json");
                        var data=json.Deserialize<Dictionary<string,object>>(response);object name;
                        if(data!=null && data.TryGetValue("title",out name) && name is string)
                        {
                            string title=((string)name).Replace('\r',' ').Replace('\n',' ').Replace('\0',' ');
                            titles[id]=title.Length>100?title.Substring(0,100):title;
                        }
                    }
                    catch { /* Playback and queue editing do not depend on optional titles. */ }
                    if(!IsDisposed)RenderQueue();
                }
            }
            finally {titleWorkers--;}
        }
        private async Task InitializeResolver(CoreWebView2Environment environment)
        {
            await resolver.EnsureCoreWebView2Async(environment);
            resolver.CoreWebView2.AddWebResourceRequestedFilter(PlayerOrigin+"/*",CoreWebView2WebResourceContext.Document);
            resolver.CoreWebView2.WebResourceRequested+=delegate(object sender,CoreWebView2WebResourceRequestedEventArgs e)
            {
                var uri=new Uri(e.Request.Uri);string id,list;long token;
                if(uri.GetLeftPart(UriPartial.Authority)!=PlayerOrigin || uri.AbsolutePath!="/resolve" ||
                    !YouTubeUrl.TryGetPlayback("~"+uri.Query.TrimStart('?'),out id,out list,out token))return;
                byte[] html=Encoding.UTF8.GetBytes(PlaylistHtml(list,token).Replace("EVENT "+token+" ","IMPORT "+token+" "));
                e.Response=environment.CreateWebResourceResponse(new System.IO.MemoryStream(html),200,"OK","Content-Type: text/html; charset=utf-8\r\nReferrer-Policy: strict-origin-when-cross-origin\r\nCache-Control: no-store\r\n");
            };
            resolver.CoreWebView2.WebMessageReceived+=delegate(object sender,CoreWebView2WebMessageReceivedEventArgs e)
            {
                if(resolveSpec==null)return;
                string[] parts=resolveSpec.Split('~');
                if(e.Source!=PlayerOrigin+"/resolve?"+parts[1]+"~"+parts[0])return;
                string message=e.TryGetWebMessageAsString();if(message.Length<=4000)Console.WriteLine(message);
            };
            resolver.CoreWebView2.NavigationStarting+=delegate(object sender,CoreWebView2NavigationStartingEventArgs e)
            {Uri uri;if(e.Uri!="about:blank" && (!Uri.TryCreate(e.Uri,UriKind.Absolute,out uri) || uri.GetLeftPart(UriPartial.Authority)!=PlayerOrigin || uri.AbsolutePath!="/resolve"))e.Cancel=true;};
            resolver.CoreWebView2.NewWindowRequested+=delegate(object s,CoreWebView2NewWindowRequestedEventArgs e){e.Handled=true;};
            resolver.CoreWebView2.PermissionRequested+=delegate(object s,CoreWebView2PermissionRequestedEventArgs e){e.State=CoreWebView2PermissionState.Deny;};
            resolver.CoreWebView2.DownloadStarting+=delegate(object s,CoreWebView2DownloadStartingEventArgs e){e.Cancel=true;};
            resolver.CoreWebView2.Settings.AreDevToolsEnabled=false;resolver.CoreWebView2.Settings.AreDefaultContextMenusEnabled=false;
        }
        private void StartResolve(string spec)
        {
            string[] parts=spec.Split('~');long token;
            if(parts.Length!=2 || !Int64.TryParse(parts[0],out token) || token<=0 || !YouTubeUrl.IsPlaylist(parts[1]))return;
            if(resolveSpec==spec && ready && resolver.CoreWebView2.Source==PlayerOrigin+"/resolve?"+parts[1]+"~"+parts[0])return;
            resolveSpec=spec;if(!ready)return;
            resolver.Visible=true;if(!Visible)PrepareBackground();
            resolver.CoreWebView2.Navigate(PlayerOrigin+"/resolve?"+parts[1]+"~"+parts[0]);
        }
        private void CancelResolve()
        {
            resolveSpec=null;resolver.Visible=false;
            if(resolver.CoreWebView2!=null)resolver.CoreWebView2.Navigate("about:blank");
            if(hideWhenPlaying && playerState==1){hideWhenPlaying=false;Hide();}
        }
    }
}
