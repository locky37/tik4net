﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace tik4net.Api
{
    public class ApiCommand: ITikCommand
    {
        private static volatile int _tagCounter = 0;
        private volatile bool _isRuning;
        private volatile int _runningTag;
        private readonly List<ITikCommandParameter> _parameters = new List<ITikCommandParameter>();
        private ApiConnection _connection;
        private string _commandText;

        public ITikConnection Connection
        {
            get { return _connection; }
            set
            {                
                Guard.ArgumentOfType<ApiConnection>(value, "Session");
                EnsureNotRunning();

                _connection = (ApiConnection)value;
            }
        }

        public string CommandText
        {
            get { return _commandText; }
            set
            {
                EnsureNotRunning();
                _commandText = value;
            }
        }

        public List<ITikCommandParameter> Parameters
        {
            get { return _parameters; }
        }

        public ApiCommand()
        {

        }

        public ApiCommand(ITikConnection connection)
        {
            Connection = connection;
        }

        public ApiCommand(ITikConnection connection, string commandText)
            :this(connection)
        {
            CommandText = commandText;
        }

        public ApiCommand(ITikConnection connection, string commandText, params ITikCommandParameter[] parameters)
            : this(connection, commandText)
        {
            _parameters.AddRange(parameters);            
        }

        private void EnsureNotRunning()
        {
            if (_isRuning)
                throw new InvalidOperationException("Command is already running.");
        }

        private void EnsureConnectionSet()
        {
            if (_connection == null)
                throw new InvalidOperationException("Connection is not assigned.");
        }

        private void EnsureCommandTextSet()
        {
            if (string.IsNullOrWhiteSpace(_commandText))
                throw new InvalidOperationException("CommandText is not set.");
        }

        private string[] ConstructCommandText()
        {
            EnsureCommandTextSet();

            return new string[] { CommandText }
                .Concat(_parameters.Select(p => string.Format("={0}={1}", p.Name, p.Value))).ToArray();
        }

        private IEnumerable<ApiSentence> EnsureApiSentences(IEnumerable<ITikSentence> sentences)
        {
            if (sentences.Any(sentence => !(sentence is ApiSentence)))
                throw new InvalidOperationException("ApiCommand expects ApiSentence as result from ApiConnection.");

            return sentences.Cast<ApiSentence>();
        }

        private ApiSentence EnsureSingleResponse(IEnumerable<ApiSentence> response)
        {
            if (response.Count() != 1)
                throw new TikConnectionException("Single response sentence expected.", this, response);

            return response.Single();
        }

        private void EnsureExcatNumberOfResponses(IEnumerable<ApiSentence> response, int nrOfResponses)
        {
            if (response.Count() != nrOfResponses)
                throw new TikConnectionException(string.Format("Command expected {0} sentences as response, but got {1} response sentences.", nrOfResponses, response.Count()), this, response);
        }

        private void ThrowPossibleResponseError(params ApiSentence[] responseSentences)
        {
            foreach (ApiSentence responseSentence in responseSentences)
            {
                ApiTrapSentence trapSentence = responseSentence as ApiTrapSentence;
                if (trapSentence != null)
                    throw new TikCommandException(this, trapSentence);
                ApiFatalSentence fatalSentence = responseSentence as ApiFatalSentence;
                if (fatalSentence != null)
                    throw new TikCommandException(this, fatalSentence.Message);
            }
        }

        private ApiDoneSentence EnsureDoneResponse(ApiSentence responseSentence)
        {
            ApiDoneSentence doneSentence = responseSentence as ApiDoneSentence;
            if (doneSentence == null)
                throw new TikConnectionException("!done sentence expected as result.", this, responseSentence);

            return doneSentence;
        }


        private void EnsureReReponse(params ApiSentence[] responseSentences)
        {
            foreach (ApiSentence responseSentence in responseSentences)
            {
                ApiReSentence reSentence = responseSentence as ApiReSentence;
                if (reSentence == null)
                    throw new TikConnectionException("!re sentence expected as result.", this, responseSentence);
            }
        }


        public void ExecuteNonQuery()
        {
            EnsureConnectionSet();
            EnsureNotRunning();

            _isRuning = true;
            try
            {
                string[] commandRows = ConstructCommandText();
                IEnumerable<ApiSentence> response = EnsureApiSentences(_connection.CallCommandSync(commandRows));
                ThrowPossibleResponseError(response.ToArray());

                ApiSentence responseSentence = EnsureSingleResponse(response);
                EnsureDoneResponse(responseSentence);
            }
            finally
            {
                _isRuning = false;
            }
        }


        public string ExecuteScalar()
        {
            EnsureConnectionSet();
            EnsureNotRunning();

            _isRuning = true;
            try
            {
                string[] commandRows = ConstructCommandText();
                IEnumerable<ApiSentence> response = EnsureApiSentences(_connection.CallCommandSync(commandRows));
                ThrowPossibleResponseError(response.ToArray());

                ApiSentence responseSentence = EnsureSingleResponse(response);
                ApiDoneSentence doneSentence =  EnsureDoneResponse(responseSentence);

                return doneSentence.GetResponseWord("ret");
            }
            finally
            {
                _isRuning = false;
            }
        }

        public ITikReSentence ExecuteSingleRow()
        {
            EnsureConnectionSet();
            EnsureNotRunning();

            _isRuning = true;
            try
            {
                string[] commandRows = ConstructCommandText();
                IEnumerable<ApiSentence> response = EnsureApiSentences(_connection.CallCommandSync(commandRows));
                ThrowPossibleResponseError(response.ToArray());

                EnsureExcatNumberOfResponses(response, 2);
                EnsureReReponse(response.First());   //!re
                ApiReSentence result = (ApiReSentence)response.First();
                EnsureDoneResponse(response.Last()); //!done

                return result;
            }
            finally
            {
                _isRuning = false;
            }
        }

        public IEnumerable<ITikReSentence> ExecuteList()
        {
            EnsureConnectionSet();
            EnsureNotRunning();

            _isRuning = true;
            try
            {
                string[] commandRows = ConstructCommandText();
                IEnumerable<ApiSentence> response = EnsureApiSentences(_connection.CallCommandSync(commandRows));
                ThrowPossibleResponseError(response.ToArray());

                EnsureReReponse(response.Take(response.Count() -1 ).ToArray());   //!re  - reapeating 
                EnsureDoneResponse(response.Last()); //!done

                return response.Take(response.Count() - 1).Cast<ApiReSentence>().ToList();
            }
            finally
            {
                _isRuning = false;
            }
        }

        public void ExecuteAsync(Action<ITikReSentence> oneResponseCallback)
        {
            EnsureConnectionSet();
            EnsureNotRunning();

            int tag = Interlocked.Increment(ref _tagCounter);
            _isRuning = true;
            _runningTag = tag;

            try
            {
                string[] commandRows = ConstructCommandText();
                _connection.CallCommandAsync(commandRows, tag.ToString(),
                                        response =>
                                        {
                                            ApiReSentence reResponse = response as ApiReSentence;
                                            if (reResponse != null)
                                                oneResponseCallback(reResponse);
                                            else 
                                            {
                                                ApiTrapSentence trapResponse = response as ApiTrapSentence;
                                                if (response is ApiDoneSentence 
                                                || ((trapResponse != null) && (trapResponse.CategoryCode == "2") && (trapResponse.Message == "interrupted"))) //listening finished
                                                {
                                                    _isRuning = false;
                                                    _runningTag = -1;
                                                }
                                                //else 
                                                //TODO Done Fail, cancel, ...
                                                //How to propagate it to users code???
                                            }
                                        });
            }
            finally
            {
                //still running
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <seealso cref="ExcecuteAsyncReader"/>
        public void Cancel()
        {
            if (_isRuning && _runningTag >= 0)
            {
                ApiCommand cancellCommand = new ApiCommand(_connection, "/cancel", new ApiCommandParameter("tag", _runningTag.ToString()));
                cancellCommand.ExecuteNonQuery();
            }
        }

        public override string ToString()
        {
            return string.Join("\n", new string[] { CommandText }.Concat(Parameters.Select(p => "  =" + p.Name + "=" + p.Value)));
        }
    }
}
