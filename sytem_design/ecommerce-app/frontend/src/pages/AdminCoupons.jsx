import { useEffect, useMemo, useState } from 'react'
import toast from 'react-hot-toast'
import axiosInstance from '../api/axiosInstance'
import Navbar from '../components/Navbar'
import './AdminCoupons.css'

const emptyForm = {
  code: '', discountType: 'Percentage', value: '', maxDiscount: '', minOrderAmount: '0',
  startsAt: '', expiresAt: '', usageLimit: '', assignedUserEmail: '',
}

const FILTERS = ['All', 'Active', 'Scheduled', 'Expired', 'Inactive']

// ---------- helpers ----------
const money = (n) => '₹' + Number(n || 0).toLocaleString('en-IN')
const asUtc = (text) => new Date(text.endsWith('Z') ? text : text + 'Z')      // the server sends UTC without the Z
const fmtDateTime = (text) =>
  text ? asUtc(text).toLocaleString('en-IN', { dateStyle: 'medium', timeStyle: 'short' }) : null
const dayLabel = (iso) =>
  new Date(iso + 'T00:00:00').toLocaleDateString('en-IN', { day: 'numeric', month: 'short' })

// UTC text from the server -> the local value a datetime-local input expects
function toLocalInput(text) {
  if (!text) return ''
  const d = asUtc(text)
  const p = (n) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}`
}

function describeOffer({ discountType, value, maxDiscount, minOrderAmount }) {
  if (!value) return 'Enter a value to see the preview'
  const off = discountType === 'Percentage' ? `${value}% off` : `${money(value)} off`
  const cap = discountType === 'Percentage' && maxDiscount ? ` up to ${money(maxDiscount)}` : ''
  const min = Number(minOrderAmount) > 0 ? ` on orders above ${money(minOrderAmount)}` : ''
  return off + cap + min
}

function validityText(c) {
  const from = fmtDateTime(c.startsAt)
  const to = fmtDateTime(c.expiresAt)
  if (!from && !to) return 'No time limit'
  if (from && to) return `${from} to ${to}`
  return from ? `From ${from}` : `Until ${to}`
}

// ---------- tiny icon set (inline SVG, no extra package) ----------
const ICONS = {
  tag: <><path d="M20.59 13.41l-7.17 7.17a2 2 0 0 1-2.83 0L2 12V2h10l8.59 8.59a2 2 0 0 1 0 2.82z" /><line x1="7" y1="7" x2="7.01" y2="7" /></>,
  check: <><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14" /><polyline points="22 4 12 14.01 9 11.01" /></>,
  bag: <><path d="M6 2L3 6v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V6l-3-4z" /><line x1="3" y1="6" x2="21" y2="6" /><path d="M16 10a4 4 0 0 1-8 0" /></>,
  percent: <><line x1="19" y1="5" x2="5" y2="19" /><circle cx="6.5" cy="6.5" r="2.5" /><circle cx="17.5" cy="17.5" r="2.5" /></>,
  close: <><line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" /></>,
  copy: <><rect x="9" y="9" width="13" height="13" rx="2" ry="2" /><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1" /></>,
  plus: <><line x1="12" y1="5" x2="12" y2="19" /><line x1="5" y1="12" x2="19" y2="12" /></>,
  search: <><circle cx="11" cy="11" r="8" /><line x1="21" y1="21" x2="16.65" y2="16.65" /></>,
}

function Icon({ name, size = 18 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor"
      strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      {ICONS[name]}
    </svg>
  )
}

function StatCard({ icon, label, value, tone }) {
  return (
    <div className="cp-stat">
      <div className={`cp-stat-icon cp-tone-${tone}`}><Icon name={icon} /></div>
      <div>
        <span className="cp-stat-label">{label}</span>
        <strong className="cp-stat-value">{value}</strong>
      </div>
    </div>
  )
}

// A slide-in panel used for both the form and the history
function Drawer({ title, subtitle, onClose, children, footer }) {
  useEffect(() => {
    const onKey = (e) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  return (
    <div className="cp-overlay" onMouseDown={onClose}>
      <aside className="cp-drawer" onMouseDown={(e) => e.stopPropagation()} role="dialog" aria-modal="true">
        <header className="cp-drawer-head">
          <div>
            <h2>{title}</h2>
            {subtitle && <p>{subtitle}</p>}
          </div>
          <button type="button" className="cp-icon-btn" onClick={onClose} aria-label="Close"><Icon name="close" /></button>
        </header>
        <div className="cp-drawer-body">{children}</div>
        {footer && <footer className="cp-drawer-foot">{footer}</footer>}
      </aside>
    </div>
  )
}

export default function AdminCoupons() {
  const [stats, setStats] = useState(null)
  const [coupons, setCoupons] = useState([])
  const [loading, setLoading] = useState(true)
  const [filter, setFilter] = useState('All')
  const [query, setQuery] = useState('')
  const [showForm, setShowForm] = useState(false)
  const [form, setForm] = useState(emptyForm)
  const [editingId, setEditingId] = useState(null)
  const [saving, setSaving] = useState(false)
  const [history, setHistory] = useState(null)        // { coupon, data }

  function loadAll() {
    Promise.all([axiosInstance.get('/coupon/stats'), axiosInstance.get('/coupon')])
      .then(([s, c]) => { setStats(s.data); setCoupons(c.data) })
      .catch(() => toast.error('Could not load coupons'))
      .finally(() => setLoading(false))
  }

  useEffect(() => {
    loadAll()
    window.addEventListener('coupons-changed', loadAll)
    return () => window.removeEventListener('coupons-changed', loadAll)
  }, [])

  const set = (field, value) => setForm((f) => ({ ...f, [field]: value }))

  function openCreate() {
    setEditingId(null)
    setForm(emptyForm)
    setShowForm(true)
  }

  function startEdit(c) {
    setEditingId(c.id)
    setForm({
      code: c.code, discountType: c.discountType, value: String(c.value),
      maxDiscount: c.maxDiscount ?? '', minOrderAmount: String(c.minOrderAmount),
      startsAt: toLocalInput(c.startsAt), expiresAt: toLocalInput(c.expiresAt),
      usageLimit: c.usageLimit ?? '', assignedUserEmail: c.assignedUserEmail ?? '',
    })
    setShowForm(true)
  }

  function closeForm() {
    setShowForm(false)
    setEditingId(null)
  }

  async function submit(e) {
    e.preventDefault()
    setSaving(true)
    const body = {
      code: form.code,
      discountType: form.discountType,
      value: parseFloat(form.value),
      maxDiscount: form.discountType === 'Percentage' && form.maxDiscount !== '' ? parseFloat(form.maxDiscount) : null,
      minOrderAmount: parseFloat(form.minOrderAmount || '0'),
      startsAt: form.startsAt ? new Date(form.startsAt).toISOString() : null,      // local time to UTC
      expiresAt: form.expiresAt ? new Date(form.expiresAt).toISOString() : null,
      usageLimit: form.usageLimit !== '' ? parseInt(form.usageLimit, 10) : null,
      assignedUserEmail: form.assignedUserEmail.trim() || null,
      isActive: true,
    }
    try {
      if (editingId) {
        const current = coupons.find((c) => c.id === editingId)
        await axiosInstance.put(`/coupon/${editingId}`, { ...body, isActive: current ? current.isActive : true })
        toast.success('Coupon updated')
      } else {
        await axiosInstance.post('/coupon', body)
        toast.success('Coupon created')
      }
      closeForm()
      loadAll()
    } catch (err) {
      toast.error(err.response?.data?.message || 'Could not save the coupon')
    } finally {
      setSaving(false)
    }
  }

  async function toggle(c) {
    if (c.isActive && !window.confirm(`Deactivate ${c.code}? Customers will no longer be able to use it.`)) return
    try {
      const res = await axiosInstance.post(`/coupon/${c.id}/toggle`)
      toast.success(res.data.message)
      loadAll()
    } catch (err) {
      toast.error(err.response?.data?.message || 'Failed')
    }
  }

  async function handleDelete(id) {
    const confirmed = window.confirm(
      'Are you sure you want to permanently delete this coupon?'
    )

    if (!confirmed) return

    try {
      const res = await axiosInstance.delete(`/coupon/${id}`)

      toast.success(res.data.message || 'Coupon deleted successfully')

      loadAll()
    } catch (err) {
      toast.error(
        err.response?.data?.message || 'Unable to delete coupon'
      )
    }
  }

  async function openHistory(coupon, pageNumber = 1) {
    try {
      const res = await axiosInstance.get(`/coupon/${coupon.id}/redemptions`, { params: { pageNumber, pageSize: 10 } })
      setHistory({ coupon, data: res.data })
    } catch {
      toast.error('Could not load the history')
    }
  }

  function copyCode(code) {
    navigator.clipboard?.writeText(code)
    toast.success(`${code} copied`)
  }

  // ---------- derived values ----------
  const counts = useMemo(() => {
    const base = { All: coupons.length, Active: 0, Scheduled: 0, Expired: 0, Inactive: 0 }
    coupons.forEach((c) => { base[c.status] = (base[c.status] || 0) + 1 })
    return base
  }, [coupons])

  const visible = useMemo(() => {
    const q = query.trim().toLowerCase()
    return coupons.filter((c) =>
      (filter === 'All' || c.status === filter) &&
      (!q || c.code.toLowerCase().includes(q) || (c.assignedUserEmail || '').toLowerCase().includes(q)))
  }, [coupons, filter, query])

  const maxDay = stats ? Math.max(1, ...stats.last7Days.map((d) => d.count)) : 1
  const topMax = stats && stats.topCoupons.length ? Math.max(...stats.topCoupons.map((t) => t.redemptions)) : 1

  return (
    <div>
      <Navbar />
      <div className="cp-shell">
        <div className="cp-wrap">

          {/* ---------- header ---------- */}
          <div className="cp-header">
            <div>
              <h1>Coupons</h1>
              <p>Create promotional offers and track how customers use them.</p>
            </div>
            <button type="button" className="cp-btn cp-btn-primary" onClick={openCreate}>
              <Icon name="plus" size={16} /> New coupon
            </button>
          </div>

          {/* ---------- stat cards ---------- */}
          <div className="cp-stats">
            <StatCard icon="tag" tone="indigo" label="Total coupons" value={stats ? stats.totalCoupons : '-'} />
            <StatCard icon="check" tone="green" label="Active now" value={stats ? stats.activeCoupons : '-'} />
            <StatCard icon="bag" tone="amber" label="Redemptions" value={stats ? stats.totalRedemptions : '-'} />
            <StatCard icon="percent" tone="rose" label="Discount given" value={stats ? money(stats.totalDiscountGiven) : '-'} />
          </div>

          {/* ---------- chart and top coupons ---------- */}
          {stats && (
            <div className="cp-analytics">
              <div className="cp-card">
                <div className="cp-card-head"><h2>Redemptions</h2><span>Last 7 days</span></div>
                <div className="cp-chart">
                  {stats.last7Days.map((d) => (
                    <div className="cp-chart-col" key={d.date}
                      title={`${d.count} redemption(s), ${money(d.discount)} discount`}>
                      <span className="cp-chart-count">{d.count}</span>
                      <div className="cp-chart-track">
                        <div className="cp-chart-bar"
                          style={{ height: `${d.count ? Math.max(8, (d.count / maxDay) * 100) : 0}%` }} />
                      </div>
                      <span className="cp-chart-label">{dayLabel(d.date)}</span>
                    </div>
                  ))}
                </div>
              </div>

              <div className="cp-card">
                <div className="cp-card-head"><h2>Top coupons</h2><span>By redemptions</span></div>
                {stats.topCoupons.length === 0 ? (
                  <p className="cp-empty-sm">No redemptions yet.</p>
                ) : (
                  <ol className="cp-top">
                    {stats.topCoupons.map((t, i) => (
                      <li key={t.code}>
                        <span className="cp-rank">{i + 1}</span>
                        <div className="cp-top-main">
                          <div className="cp-top-row">
                            <code className="cp-code">{t.code}</code>
                            <span>{t.redemptions} use{t.redemptions === 1 ? '' : 's'}</span>
                          </div>
                          <div className="cp-meter"><div style={{ width: `${(t.redemptions / topMax) * 100}%` }} /></div>
                        </div>
                      </li>
                    ))}
                  </ol>
                )}
              </div>
            </div>
          )}

          {/* ---------- coupon table ---------- */}
          <div className="cp-card cp-table-card">
            <div className="cp-toolbar">
              <div className="cp-tabs">
                {FILTERS.map((f) => (
                  <button key={f} type="button" className={`cp-tab ${filter === f ? 'is-active' : ''}`} onClick={() => setFilter(f)}>
                    {f}<span>{counts[f] || 0}</span>
                  </button>
                ))}
              </div>
              <div className="cp-search">
                <Icon name="search" size={16} />
                <input placeholder="Search code or user" value={query} onChange={(e) => setQuery(e.target.value)} />
              </div>
            </div>

            <div className="cp-table-scroll">
              <table className="cp-table">
                <thead>
                  <tr>
                    <th>Coupon</th><th>Audience</th><th>Usage</th><th>Discount given</th><th>Validity</th><th>Status</th><th></th>
                  </tr>
                </thead>
                <tbody>
                  {visible.map((c) => (
                    <tr key={c.id}>
                      <td>
                        <div className="cp-code-cell">
                          <code className="cp-code">{c.code}</code>
                          <button type="button" className="cp-icon-btn cp-icon-btn-sm" title="Copy code" onClick={() => copyCode(c.code)}>
                            <Icon name="copy" size={14} />
                          </button>
                        </div>
                        <div className="cp-sub">{describeOffer(c)}</div>
                      </td>
                      <td>
                        {c.assignedUserEmail
                          ? <span className="cp-badge" title={c.assignedUserEmail}>{c.assignedUserEmail}</span>
                          : <span className="cp-muted">Everyone</span>}
                      </td>
                      <td>
                        <div className="cp-usage">
                          <div className="cp-usage-top"><strong>{c.usedCount}</strong><span>/ {c.usageLimit ?? '∞'}</span></div>
                          {c.usageLimit && (
                            <div className="cp-meter">
                              <div style={{ width: `${Math.min(100, (c.usedCount / c.usageLimit) * 100)}%` }} />
                            </div>
                          )}
                        </div>
                      </td>
                      <td>{money(c.discountGiven)}</td>
                      <td><div className="cp-validity">{validityText(c)}</div></td>
                      <td><span className={`cp-pill cp-pill-${c.status.toLowerCase()}`}>{c.status}</span></td>
                      <td>
                        <div className="cp-actions">
                          <button type="button" className="cp-btn cp-btn-ghost cp-btn-sm" onClick={() => startEdit(c)}>Edit</button>
                          <button type="button" className="cp-btn cp-btn-ghost cp-btn-sm" onClick={() => openHistory(c)}>History</button>
                          <button type="button" className={`cp-btn cp-btn-sm ${c.isActive ? 'cp-btn-danger' : 'cp-btn-primary'}`} onClick={() => toggle(c)}>
                            {c.isActive ? 'Deactivate' : 'Activate'}
                          </button>
                          <button
                            type="button"
                            className="cp-btn cp-btn-delete cp-btn-sm"
                            onClick={() => handleDelete(c.id)}
                          >
                            Delete
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
              {loading && <div className="cp-empty">Loading coupons...</div>}
              {!loading && visible.length === 0 && (
                <div className="cp-empty">
                  {coupons.length === 0 ? 'No coupons yet. Click New coupon to create the first one.' : 'No coupons match your filters.'}
                </div>
              )}
            </div>
          </div>
        </div>
      </div>

      {/* ---------- create / edit drawer ---------- */}
      {showForm && (
        <Drawer
          title={editingId ? 'Edit coupon' : 'New coupon'}
          subtitle={editingId ? 'Changes apply to new redemptions straight away.' : 'Customers enter this code at checkout.'}
          onClose={closeForm}
          footer={
            <>
              <button type="button" className="cp-btn cp-btn-ghost" onClick={closeForm}>Cancel</button>
              <button type="submit" form="coupon-form" className="cp-btn cp-btn-primary" disabled={saving}>
                {saving ? 'Saving...' : editingId ? 'Save changes' : 'Create coupon'}
              </button>
            </>
          }
        >
          <form id="coupon-form" className="cp-form" onSubmit={submit}>
            <div className="cp-preview">
              <div className="cp-preview-value">
                {form.discountType === 'Percentage' ? `${form.value || 0}%` : money(form.value || 0)}
              </div>
              <div>
                <div className="cp-preview-code">{form.code || 'YOURCODE'}</div>
                <div className="cp-preview-text">{describeOffer(form)}</div>
              </div>
            </div>

            <div className="cp-section">
              <div className="cp-section-title">Offer</div>

              <label className="cp-field">
                <span className="cp-label">Coupon code</span>
                <input className="cp-input cp-input-mono" value={form.code} maxLength={30} required placeholder="SAVE10"
                  onChange={(e) => set('code', e.target.value.toUpperCase().replace(/[^A-Z0-9_-]/g, ''))} />
                <span className="cp-hint">Letters, numbers, dash and underscore. Customers are not case sensitive.</span>
              </label>

              <div className="cp-field">
                <span className="cp-label">Discount type</span>
                <div className="cp-seg">
                  <button type="button" className={form.discountType === 'Percentage' ? 'is-active' : ''}
                    onClick={() => set('discountType', 'Percentage')}>Percentage</button>
                  <button type="button" className={form.discountType === 'Fixed' ? 'is-active' : ''}
                    onClick={() => { set('discountType', 'Fixed'); set('maxDiscount', '') }}>Fixed amount</button>
                </div>
              </div>

              <div className="cp-row">
                <label className="cp-field">
                  <span className="cp-label">Value</span>
                  <div className="cp-input-group">
                    <span>{form.discountType === 'Percentage' ? '%' : '₹'}</span>
                    <input className="cp-input" type="number" step="0.01" min="0.01"
                      max={form.discountType === 'Percentage' ? 100 : undefined}
                      value={form.value} required onChange={(e) => set('value', e.target.value)} />
                  </div>
                </label>
                {form.discountType === 'Percentage' && (
                  <label className="cp-field">
                    <span className="cp-label">Maximum discount (optional)</span>
                    <div className="cp-input-group">
                      <span>₹</span>
                      <input className="cp-input" type="number" step="0.01" min="0"
                        value={form.maxDiscount} onChange={(e) => set('maxDiscount', e.target.value)} />
                    </div>
                  </label>
                )}
              </div>
            </div>

            <div className="cp-section">
              <div className="cp-section-title">Conditions</div>
              <div className="cp-row">
                <label className="cp-field">
                  <span className="cp-label">Minimum order</span>
                  <div className="cp-input-group">
                    <span>₹</span>
                    <input className="cp-input" type="number" step="0.01" min="0"
                      value={form.minOrderAmount} onChange={(e) => set('minOrderAmount', e.target.value)} />
                  </div>
                </label>
                <label className="cp-field">
                  <span className="cp-label">Total usage limit</span>
                  <input className="cp-input" type="number" min="1" placeholder="Unlimited"
                    value={form.usageLimit} onChange={(e) => set('usageLimit', e.target.value)} />
                </label>
              </div>
            </div>

            <div className="cp-section">
              <div className="cp-section-title">Schedule</div>
              <div className="cp-row">
                <label className="cp-field">
                  <span className="cp-label">Starts at</span>
                  <input className="cp-input" type="datetime-local" value={form.startsAt} onChange={(e) => set('startsAt', e.target.value)} />
                </label>
                <label className="cp-field">
                  <span className="cp-label">Expires at</span>
                  <input className="cp-input" type="datetime-local" value={form.expiresAt} onChange={(e) => set('expiresAt', e.target.value)} />
                </label>
              </div>
              <span className="cp-hint">Leave empty for no start or no end. Times are in your local time zone.</span>
            </div>

            <div className="cp-section">
              <div className="cp-section-title">Audience</div>
              <label className="cp-field">
                <span className="cp-label">Only for this user (optional)</span>
                <input className="cp-input" type="email" placeholder="Leave empty for everyone"
                  value={form.assignedUserEmail} onChange={(e) => set('assignedUserEmail', e.target.value)} />
                <span className="cp-hint">The email must belong to a registered user. Only that user will see and be able to use the coupon.</span>
              </label>
            </div>
          </form>
        </Drawer>
      )}

      {/* ---------- history drawer ---------- */}
      {history && (
        <Drawer
          title={`Redemptions of ${history.coupon.code}`}
          subtitle={`${history.data.totalCount} total`}
          onClose={() => setHistory(null)}
          footer={history.data.totalPages > 1 && (
            <>
              <span className="cp-page-info">Page {history.data.pageNumber} of {history.data.totalPages}</span>
              <button type="button" className="cp-btn cp-btn-ghost cp-btn-sm" disabled={history.data.pageNumber <= 1}
                onClick={() => openHistory(history.coupon, history.data.pageNumber - 1)}>Previous</button>
              <button type="button" className="cp-btn cp-btn-ghost cp-btn-sm" disabled={history.data.pageNumber >= history.data.totalPages}
                onClick={() => openHistory(history.coupon, history.data.pageNumber + 1)}>Next</button>
            </>
          )}
        >
          {history.data.items.length === 0 ? (
            <div className="cp-empty">Nobody has used this coupon yet.</div>
          ) : (
            <table className="cp-table cp-table-compact">
              <thead><tr><th>Customer</th><th>Order</th><th>Saved</th><th>Date</th></tr></thead>
              <tbody>
                {history.data.items.map((r, i) => (
                  <tr key={i}>
                    <td>{r.userName}<div className="cp-sub">{r.userEmail}</div></td>
                    <td>#{r.orderId}</td>
                    <td>{money(r.discountAmount)}</td>
                    <td>{fmtDateTime(r.redeemedAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </Drawer>
      )}
    </div>
  )
}