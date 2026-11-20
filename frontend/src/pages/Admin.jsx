import { useCallback, useEffect, useState } from 'react';
import {
  App as AntApp, Alert, Button, Card, Col, ConfigProvider, Descriptions,
  Row, Space, Spin, Statistic, Table, Tag, Typography
} from 'antd';
import {
  Store, Users, Package, Wallet, ShieldAlert, Ban, CheckCircle2,
  RotateCcw, RefreshCw, MoreHorizontal
} from 'lucide-react';
import api from '../api/client';
import { useAuth } from '../context/AuthContext';
import { money, formatDate } from '../utils/helpers';

const { Title, Text, Paragraph } = Typography;

// Ant Design's dense, token-driven layout gives this page a deliberately
// "enterprise admin" feel rather than a generic card grid. Teal keeps it in
// the same family as the rest of WashTag; CSS-in-JS is scoped to this page.
const theme = {
  token: {
    colorPrimary: '#0d9488',
    colorInfo: '#0d9488',
    colorSuccess: '#16a34a',
    colorWarning: '#d97706',
    colorError: '#dc2626',
    borderRadius: 6,
    fontSize: 13,
    controlHeight: 32,
    colorText: '#1e293b',
    colorTextSecondary: '#64748b',
    // Flat design: Ant Design's default elevation is disabled everywhere.
    boxShadow: 'none',
    boxShadowSecondary: 'none'
  },
  components: {
    Table: {
      headerBg: '#f8fafc',
      headerColor: '#475569',
      headerSplitColor: '#e2e8f0',
      rowHoverBg: '#f1f5f9',
      cellPaddingBlock: 12
    },
    Card: { paddingLG: 20 },
    Statistic: { contentFontSize: 26 },
    Button: { defaultShadow: 'none', primaryShadow: 'none', dangerShadow: 'none' }
  }
};

const STATUS_COLOR = { Active: 'green', Trial: 'blue', PastDue: 'orange', Cancelled: 'red' };

function AdminInner() {
  const { user } = useAuth();
  const { message, modal } = AntApp.useApp();
  const [stats, setStats] = useState(null);
  const [error, setError] = useState('');
  const [busyId, setBusyId] = useState(null);
  const [maintenance, setMaintenance] = useState(null);
  const [runningMaintenance, setRunningMaintenance] = useState(false);

  const load = useCallback(() => {
    api.get('/admin/stats').then(({ data }) => { setStats(data); setError(''); })
      .catch((err) => setError(err.message));
  }, []);

  useEffect(() => {
    if (user?.isAdmin) load();
  }, [user?.isAdmin, load]);

  const mutate = async (shop, path, successMessage, method = 'PATCH', body) => {
    setBusyId(shop.shopId);
    try {
      await api.request({ url: `/admin/shops/${path}`, method, data: body });
      message.success(successMessage);
      window.dispatchEvent(new CustomEvent('washtag:subscription-locked'));
      load();
    } catch (err) { message.error(err.message); }
    finally { setBusyId(null); }
  };

  const runMaintenance = async () => {
    setRunningMaintenance(true);
    try {
      const { data } = await api.post('/admin/maintenance/run');
      setMaintenance(data);
      data.errors?.length
        ? message.warning('Maintenance finished with errors.')
        : message.success('Maintenance finished.');
    } catch (err) { message.error(err.message); }
    finally { setRunningMaintenance(false); }
  };

  if (!user?.isAdmin) {
    return (
      <Alert
        type="warning"
        showIcon
        icon={<ShieldAlert size={18} />}
        message="Platform admin access required"
        description="This page is restricted to platform administrators."
      />
    );
  }
  if (error) {
    return (
      <Alert
        type="error"
        showIcon
        message="Could not load platform overview"
        description={error}
        action={<Button size="small" onClick={load}>Retry</Button>}
      />
    );
  }
  if (!stats) {
    return <div style={{ display: 'grid', placeItems: 'center', minHeight: 320 }}><Spin size="large" /></div>;
  }

  const columns = [
    {
      title: 'Shop', key: 'shop', width: 190,
      render: (_, s) => (
        <div>
          <Text strong>{s.shopName}</Text>
          <div><Text type="secondary" style={{ fontSize: 12 }}>{s.city}</Text></div>
        </div>
      )
    },
    {
      title: 'Owner', key: 'owner', width: 210,
      render: (_, s) => (
        <div>
          <div>{s.ownerName}</div>
          <Text type="secondary" style={{ fontSize: 12 }}>{s.email}</Text>
        </div>
      )
    },
    {
      title: 'Plan', dataIndex: 'plan', key: 'plan', width: 90,
      render: (plan) => <Tag bordered={false}>{plan}</Tag>
    },
    {
      title: 'Subscription', dataIndex: 'subscriptionStatus', key: 'status', width: 120,
      render: (status) => <Tag bordered={false} color={STATUS_COLOR[status] || 'default'}>{status}</Tag>
    },
    {
      title: 'Orders', dataIndex: 'orderCount', key: 'orders', width: 80, align: 'right',
      sorter: (a, b) => a.orderCount - b.orderCount
    },
    {
      title: 'Collected', dataIndex: 'collected', key: 'collected', width: 120, align: 'right',
      render: (value) => <Text strong>{money(value)}</Text>,
      sorter: (a, b) => a.collected - b.collected
    },
    {
      title: 'Renews', dataIndex: 'renewalDate', key: 'renews', width: 130,
      render: (value) => <Text type="secondary">{formatDate(value)}</Text>
    },
    {
      title: 'Actions', key: 'actions',
      render: (_, s) => {
        const busy = busyId === s.shopId;
        return (
          <Space size={4} wrap>
            {s.isActive ? (
              <Button
                size="small" type="text" icon={<Ban size={14} />} loading={busy}
                onClick={() => modal.confirm({
                  title: `Suspend ${s.shopName}?`,
                  content: 'Its owners will lose access to daily operations until an admin reactivates it.',
                  okText: 'Suspend', okButtonProps: { danger: true },
                  onOk: () => mutate(s, `${s.shopId}/active`, 'Shop suspended.', 'PATCH', { active: false })
                })}
              >Suspend</Button>
            ) : (
              <Button
                size="small" type="text" icon={<CheckCircle2 size={14} />} loading={busy}
                onClick={() => mutate(s, `${s.shopId}/active`, 'Shop reactivated.', 'PATCH', { active: true })}
              >Activate</Button>
            )}
            <Button
              size="small" type="text" loading={busy} disabled={!s.hasPendingInvoice}
              title={s.hasPendingInvoice ? 'Settle the oldest unpaid invoice' : 'No unpaid invoice'}
              onClick={() => mutate(s, `${s.shopId}/settle`, 'Invoice settled.', 'POST')}
            >Settle invoice</Button>
            {s.subscriptionStatus === 'Cancelled' || s.subscriptionStatus === 'PastDue' ? (
              <Button
                size="small" type="text" icon={<RotateCcw size={14} />} loading={busy}
                onClick={() => modal.confirm({
                  title: `Restore the plan for ${s.shopName}?`,
                  content: 'This starts a fresh subscription term and issues a payable invoice.',
                  onOk: () => mutate(s, `${s.shopId}/restore`, 'Plan restored.')
                })}
              >Restore</Button>
            ) : (
              <Button
                size="small" type="text" danger icon={<MoreHorizontal size={14} />} loading={busy}
                onClick={() => modal.confirm({
                  title: `Cancel the plan for ${s.shopName}?`,
                  content: 'This locks the shop until an admin explicitly restores its plan.',
                  okText: 'Cancel plan', okButtonProps: { danger: true },
                  onOk: () => mutate(s, `${s.shopId}/cancel`, 'Subscription cancelled.')
                })}
              >Cancel plan</Button>
            )}
          </Space>
        );
      }
    }
  ];

  return (
    <div style={{ display: 'grid', gap: 20 }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 16, flexWrap: 'wrap' }}>
        <div>
          <Title level={3} style={{ margin: 0 }}>Platform overview</Title>
          <Paragraph type="secondary" style={{ margin: '4px 0 0' }}>
            All shops on your WashTag BD deployment.
          </Paragraph>
        </div>
        <Button icon={<RefreshCw size={14} />} loading={runningMaintenance} onClick={runMaintenance}>
          Run maintenance
        </Button>
      </div>

      {maintenance && (
        <>
        <Title level={5} style={{ margin: 0 }}>Last maintenance run</Title>
        <Alert
          type={maintenance.errors?.length ? 'warning' : 'success'}
          showIcon
          message={new Date(maintenance.ranAt).toLocaleString()}
          description={
            <Descriptions size="small" column={{ xs: 1, sm: 2, lg: 4 }} bordered>
              <Descriptions.Item label="Overdue invoices">{maintenance.overdueInvoices}</Descriptions.Item>
              <Descriptions.Item label="Expired subscriptions">{maintenance.expiredSubscriptions}</Descriptions.Item>
              <Descriptions.Item label="Messages pruned">{maintenance.prunedMessages}</Descriptions.Item>
              <Descriptions.Item label="Shops reindexed">{maintenance.reindexedShops}</Descriptions.Item>
            </Descriptions>
          }
        />
        </>
      )}
      {maintenance?.errors?.length > 0 && (
        <Alert type="error" showIcon message="Maintenance errors"
          description={<ul style={{ margin: 0, paddingLeft: 18 }}>{maintenance.errors.map((e, i) => <li key={i}>{e}</li>)}</ul>} />
      )}

      <Row gutter={[16, 16]}>
        {[
          { icon: Store, label: 'Shops', value: stats.shopCount, hint: `${stats.activeShopCount} active` },
          { icon: Users, label: 'Accounts', value: stats.userCount, hint: 'Owners + staff' },
          { icon: Package, label: 'Orders', value: stats.orderCount, hint: 'Across all shops' },
          { icon: Wallet, label: 'Collected', value: money(stats.collected), hint: 'All-time payments' }
        ].map(({ icon: Icon, label, value, hint }) => (
          <Col xs={24} sm={12} lg={6} key={label}>
            <Card size="small" styles={{ body: { padding: 18 } }}>
              <Statistic
                title={<Space size={6}><Icon size={15} color="#64748b" />{label}</Space>}
                value={value}
              />
              <Text type="secondary" style={{ fontSize: 12 }}>{hint}</Text>
            </Card>
          </Col>
        ))}
      </Row>

      <Card size="small" title="Shops" extra={<Text type="secondary">Subscription and activity by tenant</Text>}>
        <Table
          rowKey="shopId"
          columns={columns}
          dataSource={stats.shops}
          size="middle"
          pagination={false}
          scroll={{ x: 1100 }}
        />
      </Card>
    </div>
  );
}

export default function Admin() {
  return (
    <ConfigProvider theme={theme}>
      <AntApp>
        <AdminInner />
      </AntApp>
    </ConfigProvider>
  );
}
