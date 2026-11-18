import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import {
  Sparkles, Send, Mic, MicOff, Bot, UserRound, Database, RefreshCw,
  ExternalLink, Lightbulb, CircleCheck, CircleAlert, WandSparkles,
  ThumbsUp, ThumbsDown, Trash2
} from 'lucide-react';
import api from '../api/client';
import { useToast } from '../context/ToastContext';
import { useLanguage } from '../context/LanguageContext';
import { formatDateTime } from '../utils/helpers';

const WELCOME = {
  role: 'assistant',
  answer: 'Salam! I am your shop assistant. I can search your orders, customers, payments and pickup delays. Ask me anything in English or Banglish.',
  suggestions: [
    "Show me customers who haven't picked up their clothes in 5 days",
    'What is the total unpaid bill for Mr. Rahman?',
    'How many orders are currently in progress?'
  ]
};

export default function Assistant() {
  const [messages, setMessages] = useState([WELCOME]);
  const [question, setQuestion] = useState('');
  const [busy, setBusy] = useState(false);
  const [listening, setListening] = useState(false);
  const [status, setStatus] = useState(null);
  const recognitionRef = useRef(null);
  const bottomRef = useRef(null);
  const busyRef = useRef(false);
  const askRef = useRef(null);
  const toast = useToast();
  const { t } = useLanguage();

  useEffect(() => {
    api.get('/assistant/status').then(({ data }) => setStatus(data)).catch(() => {});
    api.get('/assistant/history?limit=50').then(({ data }) => {
      if (!data.length) return;
      setMessages((current) => {
        const saved = data.map((item) => ({ ...item, text: item.answer }));
        const savedIds = new Set(saved.map((item) => item.id));
        const fresh = current.filter((item) => item !== WELCOME && !savedIds.has(item.id));
        return [WELCOME, ...saved, ...fresh];
      });
    }).catch(() => {});
  }, []);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth', block: 'end' });
  }, [messages, busy]);

  const ask = async (text) => {
    const prompt = (text || question).trim();
    if (!prompt || busyRef.current) return;
    busyRef.current = true;
    setMessages((current) => [...current, { role: 'user', text: prompt }]);
    setQuestion('');
    setBusy(true);
    try {
      const { data } = await api.post('/assistant/ask', { question: prompt }, { timeout: 60000 });
      setMessages((current) => [...current, { role: 'assistant', ...data }]);
    } catch (error) {
      setMessages((current) => [
        ...current,
        { role: 'assistant', answer: `Sorry, I could not answer that. ${error.message}` }
      ]);
    } finally {
      busyRef.current = false;
      setBusy(false);
    }
  };
  askRef.current = ask;

  useEffect(() => () => {
    try { recognitionRef.current?.abort(); } catch { /* ignore */ }
  }, []);

  const toggleMic = () => {
    if (listening) {
      recognitionRef.current?.stop();
      setListening(false);
      return;
    }
    const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
    if (!Recognition) {
      toast.info('Voice input is not supported in this browser. Please type your question.');
      return;
    }
    try {
      const recognition = new Recognition();
      recognition.lang = 'en-US';
      recognition.interimResults = false;
      recognition.maxAlternatives = 1;
      recognition.onresult = (event) => {
        const transcript = event.results[0][0].transcript;
        setQuestion(transcript);
        askRef.current?.(transcript);
      };
      recognition.onerror = () => {
        setListening(false);
        toast.error('I could not hear you. Try again or type your question.');
      };
      recognition.onend = () => setListening(false);
      recognitionRef.current = recognition;
      recognition.start();
      setListening(true);
    } catch {
      toast.error('Could not start the microphone.');
    }
  };

  const reindex = async () => {
    try {
      const { data } = await api.post('/assistant/reindex', null, { timeout: 120000 });
      toast.success(`Index refreshed — ${data.documents} document(s) ready.`);
      const { data: fresh } = await api.get('/assistant/status');
      setStatus(fresh);
    } catch (error) {
      toast.error(error.message);
    }
  };

  const sendFeedback = async (message, rating) => {
    if (!message.id) return;
    try {
      await api.post('/assistant/feedback', { messageId: message.id, rating });
      setMessages((current) => current.map((item) =>
        item.id === message.id ? { ...item, rating } : item));
      toast.success(rating > 0 ? t('Thanks for the feedback!') : t('Thanks for the feedback!'));
    } catch (error) { toast.error(error.message); }
  };

  const clearHistory = async () => {
    try {
      await api.delete('/assistant/history');
      setMessages([WELCOME]);
      toast.success(t('Conversation cleared.'));
    } catch (error) { toast.error(error.message); }
  };

  const useSuggestion = (text) => ask(text);

  return (
    <div className="page-stack assistant-page">
      <div className="page-heading">
        <div>
          <h2>{t('AI Shop Assistant')}</h2>
          <p>{t("Ask questions in English or Banglish — answers come from your shop's own data.")}</p>
        </div>
      </div>

      <div className="assistant-grid">
        <section className="card chat-card">
          <header className="chat-header">
            <div className="chat-header-identity">
              <span className="chat-header-icon"><WandSparkles size={20} /></span>
              <div>
                <strong>WashTag Assistant</strong>
                <small>{status?.VectorReady ? t('Vector index online') : t('Using database facts')} · {status?.Provider || 'Connecting…'}</small>
              </div>
            </div>
            <div className="chat-header-actions">
              {status && (
                <span className={`badge ${status.VectorReady ? 'payment-Paid' : 'payment-Partial'}`}>
                  <i className="badge-dot" /> {status.DocumentCount} docs
                </span>
              )}
              <button className="icon-btn" onClick={clearHistory} title={t('Clear history')}><Trash2 size={16} /></button>
            </div>
          </header>

          <div className="chat-messages">
            {messages.map((message, index) => (
              <div key={message.id && message.id !== '00000000-0000-0000-0000-000000000000'
                ? message.id : `${message.role}-${index}`} className={`chat-row ${message.role}`}>
                <span className="chat-avatar">
                  {message.role === 'user' ? <UserRound size={17} /> : <Bot size={17} />}
                </span>
                <div className="chat-bubble">
                  <p>{message.answer || message.text}</p>

                  {message.intent && <span className="chat-intent">{t('Understood')}: {message.intent}</span>}

                  {message.sources?.length > 0 && (
                    <div className="chat-sources">
                      <span className="chat-sources-label"><Database size={13} /> {t('Based on these records')}</span>
                      {message.sources.map((source, sourceIndex) => (
                        <div className="chat-source" key={`${source.refId}-${sourceIndex}`}>
                          {source.type === 'order' ? (
                            <Link to={`/app/orders/${source.refId}`}>
                              <ExternalLink size={13} /> {source.title} <span>· {Math.round(source.similarity * 100)}% match</span>
                            </Link>
                          ) : (
                            <span className="chat-source-static">
                              <span className="soft-chip">{source.type}</span> {source.title}
                              <span> · {Math.round(source.similarity * 100)}% match</span>
                            </span>
                          )}
                        </div>
                      ))}
                    </div>
                  )}

                  {message.suggestions?.length > 0 && index === 0 && (
                    <div className="chat-suggestions-inline">
                      {message.suggestions.map((suggestion) => (
                        <button key={suggestion} onClick={() => useSuggestion(suggestion)}>{suggestion}</button>
                      ))}
                    </div>
                  )}

                  {message.role === 'assistant' && message.id && (
                    <div className="chat-feedback">
                      <button
                        className={message.rating === 1 ? 'selected' : ''}
                        onClick={() => sendFeedback(message, 1)}
                        title={t('Good answer')}
                      ><ThumbsUp size={14} /></button>
                      <button
                        className={message.rating === -1 ? 'selected' : ''}
                        onClick={() => sendFeedback(message, -1)}
                        title={t('Bad answer')}
                      ><ThumbsDown size={14} /></button>
                    </div>
                  )}
                  {message.answeredAt && <time>{formatDateTime(message.answeredAt)}</time>}
                </div>
              </div>
            ))}

            {busy && (
              <div className="chat-row assistant">
                <span className="chat-avatar"><Bot size={17} /></span>
                <div className="chat-bubble"><span className="typing"><i /><i /><i /></span></div>
              </div>
            )}
            <div ref={bottomRef} />
          </div>

          <form
            className="chat-input"
            onSubmit={(event) => { event.preventDefault(); ask(question); }}
          >
            <button
              type="button"
              className={`icon-btn mic-btn ${listening ? 'mic-live' : ''}`}
              onClick={toggleMic}
              title={listening ? 'Stop listening' : 'Speak your question'}
            >
              {listening ? <MicOff size={18} /> : <Mic size={18} />}
            </button>
            <input
              value={question}
              onChange={(event) => setQuestion(event.target.value)}
              aria-label="Ask the assistant"
              placeholder={listening ? 'Listening…' : 'Ask about unpaid bills, pickups, customers…'}
            />
            <button type="submit" className="btn btn-primary" disabled={busy || !question.trim()}>
              <Send size={16} /> <span>Ask</span>
            </button>
          </form>
        </section>

        <aside className="assistant-aside">
          <section className="card info-panel">
            <div className="panel-heading"><Database size={19} /><h3>Knowledge base</h3></div>
            <div className="assistant-stat">
              <span>Indexed documents</span>
              <strong>{status?.DocumentCount ?? '—'}</strong>
            </div>
            <div className="assistant-stat">
              <span>Embeddings</span>
              <strong className="small-value">{status?.EmbeddingModel || '—'}</strong>
            </div>
            <div className="assistant-stat">
              <span>Answer engine</span>
              <strong className="small-value">{status?.Provider || '—'}</strong>
            </div>
            <div className={`assistant-health ${status?.VectorReady ? 'healthy' : ''}`}>
              {status?.VectorReady ? <CircleCheck size={16} /> : <CircleAlert size={16} />}
              {status?.VectorReady ? 'pgvector search is live' : (status?.Message || 'Checking index…')}
            </div>
            <button className="btn btn-outline btn-sm btn-block" onClick={reindex}>
              <RefreshCw size={15} /> Rebuild index
            </button>
          </section>

          <section className="card info-panel">
            <div className="panel-heading"><Lightbulb size={19} /><h3>Try asking</h3></div>
            <div className="assistant-prompts">
              {[
                "Koto customers er kapor 5 din dhore porchha?",
                'Total unpaid bill for Mr. Rahman?',
                'How much money collected this month?',
                'Show orders waiting for pickup'
              ].map((prompt) => (
                <button key={prompt} onClick={() => useSuggestion(prompt)}>{prompt}</button>
              ))}
            </div>
          </section>

          <section className="card info-panel">
            <div className="panel-heading"><Sparkles size={19} /><h3>How it works</h3></div>
            <ol className="how-list">
              <li>Orders and customers are embedded into PostgreSQL with pgvector.</li>
              <li>Your question is matched against those records (RAG retrieval).</li>
              <li>Answers are grounded in real database facts — never guessed.</li>
            </ol>
            <p className="tiny-note">No API key needed. Add OPENAI_API_KEY in .env for LLM-phrased answers.</p>
          </section>
        </aside>
      </div>
    </div>
  );
}
