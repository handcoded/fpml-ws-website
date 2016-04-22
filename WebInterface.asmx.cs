//==============================================================================
//  _____      __  __ _                                      
// |  ___| __ |  \/  | |                                     
// | |_ | '_ \| |\/| | |                                     
// |  _|| |_) | |  | | |___                                  
// |_|  | .__/|_| _|_|_____|                 _               
// \ \  |_| / /__| |__/ ___|  ___ _ ____   _(_) ___ ___  ___ 
//  \ \ /\ / / _ \ '_ \___ \ / _ \ '__\ \ / / |/ __/ _ \/ __|
//   \ V  V /  __/ |_) |__) |  __/ |   \ V /| | (_|  __/\__ \
//    \_/\_/ \___|_.__/____/ \___|_|    \_/ |_|\___\___||___/
//                                                           
//
//==============================================================================
// Copyright (C)2014-2016 ISDA and HandCoded Software Ltd.
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions
// are met:
//
// 1. Redistributions of source code must retain the above copyright
//    notice, this list of conditions and the following disclaimer.
// 2. Redistributions in binary form must reproduce the above copyright
//    notice, this list of conditions and the following disclaimer in the
//    documentation and/or other materials provided with the distribution.
//
// THIS SOFTWARE IS PROVIDED BY THE AUTHOR AND CONTRIBUTORS ''AS IS'' AND
// ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
// ARE DISCLAIMED.  IN NO EVENT SHALL THE AUTHOR OR CONTRIBUTORS BE LIABLE
// FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
// DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS
// OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION)
// HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT
// LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY
// OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF
// SUCH DAMAGE.
//
// This work is licensed under a Creative Commons Attribution 4.0 International
// License. See http://creativecommons.org/licenses/by/4.0/ for details.
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web;
using System.Web.Services;
using System.Web.Services.Protocols;
using System.Xml;

using FpML;
using FpML.Engine;
using FpML.WebService;

using HandCoded.Xml;
using HandCoded.Xml.Resolver;

using log4net.Config;

namespace WebServices
{
    /// <summary>
    /// The <b>WebInterface</b> class performs basic connection state
    /// management while passing requests to the underlying
    /// <see cref="ValidationEngine"/> instance for processing/
    /// </summary>
    [WebService(Namespace = "http://app.fpml.org/ws")]
    [WebServiceBinding(ConformsTo = WsiProfiles.BasicProfile1_1)]
    [System.ComponentModel.ToolboxItem(false)]
    [System.Web.Script.Services.ScriptService]
    public class WebInterface : System.Web.Services.WebService, IAuthorise, ISynchronous, IAsynchronous, INotification
    {
        /// <summary>
        /// Establishes a session if the login credentials provided are valid.
        /// This implementation only checks that the username looks like a
        /// valid email address.
        /// </summary>
        /// <param name="username">The username</param>
        /// <param name="password">The password</param>
        /// <returns><c>true</c> if the session was started.</returns>
        [WebMethod(MessageName="StartSession",EnableSession=true)]
        public bool StartSession(string username, string password)
        {
            if (emailPattern.IsMatch (username))
            {
                UserContext context = UserContext.ForUser(username);

                Session.Add("UserContext", context);
                engine.StartSession(context);

                return (true);
            }

            throw new SoapException("Invalid credentials - Use your email address",
                SoapException.ClientFaultCode, Context.Request.Url.AbsolutePath);
        }

        /// <summary>
        /// Terminates the current session. This implementation removes the
        /// <see cref="UserContext"/> object associated with the session.
        /// </summary>
        [WebMethod(MessageName = "EndSession", EnableSession = true)]
        public void EndSession()
        {
            UserContext context = GetUserContext();

            if (context != null)
                engine.EndSession (context);

            Session.Remove("UserContext");
        }

        //----------------------------------------------------------------------

        /// <summary>
        /// Submits an FpML document for processing and returns a reponse
        /// message identifier that can be used for correlation.
        /// </summary>
        /// <param name="document">The FpML message document to process.</param>
        /// <returns>The unique response message correlation identifier.</returns>
        [WebMethod(MessageName = "SubmitMessage", EnableSession = true)]
        public string SubmitMessage (string document)
        {
            UserContext context = GetUserContext ();
            Message message = new Message (document);

            context.Responses.Enqueue (engine.Process (context, message));
            return (message.ResponseId);
        }

        /// <summary>
        /// Returns the next response message optionally allowing the
        /// caller to wait if the queue is empty.
        /// </summary>
        /// <param name="wait">Indicates if the call should wait.</param>
        /// <returns>The next response message or <c>null</c> if there
        /// are none in the queue.</returns>
        [WebMethod(MessageName = "RetrieveMessage", EnableSession = true)]
        public string RetrieveMessage (bool wait)
        {
            UserContext context = GetUserContext();
            Message result = context.Responses.Dequeue(wait);

            return ((result != null) ? result.Payload : null);
        }

        /// <summary>
        /// Returns a specific response message based on the identifier
        /// returned at the time of request submission.
        /// </summary>
        /// <param name="requestId">The unique correlation identifier.</param>
        /// <param name="wait">Indicates if the call should wait.</param>
        /// <returns>The next response message or <c>null</c> if there
        /// are none in the queue.</returns>
        [WebMethod(MessageName = "RetrieveResponseMessage", EnableSession = true)]
        public string RetrieveResponseMessage(string requestId, bool wait)
        {
            UserContext context = GetUserContext();
            Message result = context.Responses.Dequeue (requestId, wait);

            return ((result != null) ? result.Payload : null);
        }

        //----------------------------------------------------------------------

        /// <summary>
        /// Causes the indicated message to be processed and the response
        /// returned to the caller.
        /// </summary>
        /// <param name="message">The FpML message document to process.</param>
        /// <returns>The FpML response message document.</returns>
        [WebMethod(MessageName = "ProcessMessage", EnableSession = true)]
        public string ProcessMessage(string message)
        {
            return (RetrieveResponseMessage (SubmitMessage (message), true));
        }

        //----------------------------------------------------------------------

        /// <summary>
        /// Returns the next notification message optionally allowing the
        /// caller to wait if the queue is empty.
        /// </summary>
        /// <param name="wait">Indicates if the call should wait.</param>
        /// <returns>The next notification message or <c>null</c> if there
        /// are none in the queue.</returns>
        [WebMethod(MessageName = "RetrieveNotificationMessage", EnableSession = true)]
        public string RetrieveNotificationMessage(bool wait)
        {
            UserContext context = GetUserContext();
            Message result = context.Notifications.Dequeue(wait);

            return ((result != null) ? result.Payload : null);
        }

        //----------------------------------------------------------------------

        /// <summary>
        /// Application instance.
        /// </summary>
        private static WebApplication webApplication = new WebApplication ();

        /// <summary>
        /// A regular expression used to detect a reasonable looking email address.
        /// </summary>
        private static Regex emailPattern = new Regex ("^[a-zA-Z0-9_.+-]+@[a-zA-Z0-9-]+\\.[a-zA-Z0-9-.]+$");

        /// <summary>
        /// The engine to which received messages will be sent for processing.
        /// </summary>
        private static ValidationEngine engine = new ValidationEngine();

        /// <summary>
        /// Attempts to recover the <see cref="UserContext"/> instance associated with
        /// the current HTTP session.
        /// </summary>
        /// <returns>The <see cref="UserContext"/> for the session</returns>
        /// <exception cref="SoapException">Thrown if no <see cref="UserContext"/>
        /// has been associated with the session.</exception>
        private UserContext GetUserContext()
        {
            UserContext context = Session["UserContext"] as UserContext;

            if (context == null)
                throw new SoapException("No user context - Have you started a session?",
                    SoapException.ClientFaultCode, Context.Request.Url.AbsolutePath);

            return (context);
        }

        //----------------------------------------------------------------------

        static WebInterface()
        {
            // log4net.Config.DOMConfigurator.Configure();

            webApplication.Run();
        }
    }
}