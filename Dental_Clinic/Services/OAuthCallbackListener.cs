using System.Net;
using System.Text;
using System.Diagnostics;

namespace Dental_Clinic.Services
{
    public class OAuthCallbackListener
    {
        private HttpListener? _listener;
        private TaskCompletionSource<string>? _callbackReceived;

        // Start listening and return a task that completes when callback arrives
        public Task<string?> StartAsync(int port = 5000)
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _callbackReceived = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                _listener.Start();
            }
            catch (HttpListenerException ex)
            {
                // Cannot start (port busy or permissions). Fail fast.
                _callbackReceived.TrySetResult(string.Empty);
                return _callbackReceived.Task;
            }

            _ = ListenLoop();
            return _callbackReceived.Task;
        }

        private async Task ListenLoop()
        {
            try
            {
                var context = await _listener!.GetContextAsync();
                var request = context.Request;
                var response = context.Response;
                var query = request.Url?.Query ?? string.Empty;

                // Send success HTML page
                var successHtml = @"<!DOCTYPE html>
<html><head><meta charset='UTF-8'><title>Login Successful</title>
<style>*{margin:0;padding:0;box-sizing:border-box}body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;background:linear-gradient(135deg,#0d6efd 0%,#00b4d8 100%);display:flex;align-items:center;justify-content:center;min-height:100vh;color:white}.container{text-align:center;padding:40px;max-width:500px}.success-icon{width:100px;height:100px;margin:0 auto 24px;background:rgba(255,255,255,0.2);border-radius:50%;display:flex;align-items:center;justify-content:center;animation:scaleIn 0.5s ease-out}.checkmark{width:50px;height:50px;stroke:white;stroke-width:3;fill:none;animation:draw 0.8s ease-out 0.3s forwards;stroke-dasharray:100;stroke-dashoffset:100}h1{font-size:32px;font-weight:700;margin-bottom:12px;animation:fadeInUp 0.6s ease-out 0.4s both}p{font-size:18px;opacity:0.95;margin-bottom:24px;animation:fadeInUp 0.6s ease-out 0.5s both}.info{background:rgba(255,255,255,0.15);backdrop-filter:blur(10px);border-radius:12px;padding:20px;margin-bottom:24px;border:1px solid rgba(255,255,255,0.2);animation:fadeInUp 0.6s ease-out 0.6s both}.btn{display:inline-block;background:white;color:#0d6efd;padding:14px 32px;border-radius:8px;text-decoration:none;font-weight:600;font-size:16px;transition:all 0.3s;animation:fadeInUp 0.6s ease-out 0.7s both;cursor:pointer;border:none;box-shadow:0 4px 12px rgba(0,0,0,0.15)}.btn:hover{transform:translateY(-2px);box-shadow:0 6px 20px rgba(0,0,0,0.2)}.auto-close{font-size:13px;opacity:0.8;margin-top:16px;animation:fadeInUp 0.6s ease-out 0.8s both}@keyframes scaleIn{from{transform:scale(0);opacity:0}to{transform:scale(1);opacity:1}}@keyframes draw{to{stroke-dashoffset:0}}@keyframes fadeInUp{from{opacity:0;transform:translateY(20px)}to{opacity:1;transform:translateY(0)}}</style>
</head><body>
<div class='container'><div class='success-icon'><svg class='checkmark' viewBox='0 0 52 52'><path d='M14 27l7 7 16-16'/></svg></div>
<h1>✅ Successfully Authenticated!</h1><p>Your Google account has been linked</p>
<div class='info'><p>You can now close this window and return to the Dental Clinic app.<br><br>Your session is being set up...</p></div>
<button class='btn' onclick='window.close()'>Close Window</button>
<p class='auto-close'>This window will close automatically in <span id='countdown'>3</span> seconds</p></div>
<script>let s=3;const c=document.getElementById('countdown');const i=setInterval(()=>{s--;c.textContent=s;if(s<=0){clearInterval(i);window.close()}},1000)</script>
</body></html>";

                var buffer = System.Text.Encoding.UTF8.GetBytes(successHtml);
                response.ContentType = "text/html";
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.Close();

                Debug.WriteLine($"[Listener] Sent success page, returning query: {query}");
                _callbackReceived.SetResult(query ?? string.Empty);
            }
            catch (ObjectDisposedException)
            {
                // Listener stopped before callback; return empty so caller can handle.
                _callbackReceived!.TrySetResult(string.Empty);
            }
            catch
            {
                _callbackReceived!.TrySetResult(string.Empty);
            }
            finally
            {
                Stop();
            }
        }

        public void Stop()
        {
            try
            {
                _listener?.Close();
            }
            catch { }
            finally
            {
                _listener = null;
            }
        }
    }
}
