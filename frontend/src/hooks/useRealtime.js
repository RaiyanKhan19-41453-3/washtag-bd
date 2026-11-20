import { useEffect, useRef } from 'react';
import * as signalR from '@microsoft/signalr';

/**
 * Subscribes to the server's realtime order events.
 * group: { type: 'shop' } or { type: 'token', value: '<guid>' }
 */
export function useRealtime({ token = null, group = { type: 'shop' }, onUpdate, enabled = true }) {
  const callbackRef = useRef(onUpdate);
  callbackRef.current = onUpdate;

  useEffect(() => {
    if (!enabled) return undefined;

    // Read storage at connect/reconnect time so a refreshed access token is used.
    const options = token
      ? { accessTokenFactory: () => localStorage.getItem('washtag_token') ?? token ?? '' }
      : {};
    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/orders', options)
      .withAutomaticReconnect()
      .build();

    connection.on('OrderUpdated', (payload) => callbackRef.current?.(payload));

    const join = () => group.type === 'token' && group.value
      ? connection.invoke('JoinTokenGroup', group.value)
      : connection.invoke('JoinShopGroup');

    // A reconnect gets a new ConnectionId, so token groups must be rejoined.
    connection.onreconnected(() => { join().catch(() => {}); });

    let stopped = false;
    let retryTimer = null;
    const connect = () => connection.start().then(join).catch(() => {
      // withAutomaticReconnect only retries after a successful start. Retry the
      // initial connection ourselves so a brief hub outage is not permanent.
      if (!stopped && !retryTimer)
        retryTimer = window.setTimeout(() => { retryTimer = null; connect(); }, 5000);
    });
    connect();

    return () => {
      stopped = true;
      if (retryTimer) window.clearTimeout(retryTimer);
      connection.stop().catch(() => {});
    };
  }, [token, group?.type, group?.value, enabled]);
}
