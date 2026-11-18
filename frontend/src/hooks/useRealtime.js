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

    const options = token ? { accessTokenFactory: () => token } : {};
    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/orders', options)
      .withAutomaticReconnect()
      .build();

    connection.on('OrderUpdated', (payload) => callbackRef.current?.(payload));

    connection.start()
      .then(() => {
        if (group.type === 'token' && group.value) return connection.invoke('JoinTokenGroup', group.value);
        return connection.invoke('JoinShopGroup');
      })
      .catch(() => { /* Polling fallback keeps the UI fresh. */ });

    return () => {
      connection.stop().catch(() => {});
    };
  }, [token, group?.type, group?.value, enabled]);
}
