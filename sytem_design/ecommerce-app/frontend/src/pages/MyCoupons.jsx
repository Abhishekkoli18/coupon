import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import toast from 'react-hot-toast'
import axiosInstance from '../api/axiosInstance'
import Navbar from '../components/Navbar'
import './MyCoupons.css'

const money = (n) => '₹' + Number(n || 0).toLocaleString('en-IN')
const asUtc = (t) => new Date(t.endsWith('Z') ? t : t + 'Z')          // the server sends UTC without the Z
const fmtDate = (t) => asUtc(t).toLocaleDateString('en-IN', { day: 'numeric', month: 'short', year: 'numeric' })
const fmtDateTime = (t) => asUtc(t).toLocaleString('en-IN', { dateStyle: 'medium', timeStyle: 'short' })

function headline(c) {
  const off = c.discountType === 'Percentage' ? `${c.value}% off` : `Flat ${money(c.value)} off`
  return c.maxDiscount ? `${off}, up to ${money(c.maxDiscount)}` : off
}

export default function MyCoupons() {
  const [forYou, setForYou] = useState([])
  const [publicList, setPublicList] = useState([])
  const [history, setHistory] = useState([])
  const [loading, setLoading] = useState(true)
  const [copied, setCopied] = useState('')

  function load() {
    Promise.all([
      axiosInstance.get('/coupon/mine'),
      axiosInstance.get('/coupon/public'),
      axiosInstance.get('/coupon/my-redemptions'),
    ])
      .then(([mine, pub, hist]) => {
        setForYou(mine.data)
        setPublicList(pub.data)
        setHistory(hist.data)
      })
      .catch(() => toast.error('Could not load coupons'))
      .finally(() => setLoading(false))
  }

  useEffect(() => {
    load()
    window.addEventListener('coupons-changed', load)      // fired by CouponNotifier
    return () => window.removeEventListener('coupons-changed', load)
  }, [])

  function copy(code) {
    navigator.clipboard?.writeText(code)
    toast.success(`${code} copied. Paste it at checkout.`)
    setCopied(code)
    setTimeout(() => setCopied(''), 2000)
  }

  const usedCodes = new Set(history.map((h) => h.code))
  const personalCodes = new Set(forYou.map((c) => c.code))
  const available = [...forYou, ...publicList.filter((c) => !usedCodes.has(c.code))]
  const totalSaved = history.reduce((sum, h) => sum + h.discountAmount, 0)

  return (
    <div>
      <Navbar />
      <div className="mc-shell">
        <div className="mc-wrap">

          <div className="mc-header">
            <h1>My Coupons</h1>
            <p>Copy a code and paste it at checkout. Each coupon can be used once.</p>
          </div>

          {/* ---------- available coupons ---------- */}
          <div className="mc-card">
            <div className="mc-card-head">
              <h2>Available now {!loading && <span className="mc-count">{available.length}</span>}</h2>
            </div>

            {loading ? (
              <div className="mc-empty">Loading coupons...</div>
            ) : available.length === 0 ? (
              <div className="mc-empty">
                <strong>No coupons available right now</strong>
                New offers will appear here as soon as they go live.
              </div>
            ) : (
              <div className="mc-grid">
                {available.map((c) => (
                  <div className={`mc-ticket ${personalCodes.has(c.code) ? 'is-personal' : ''}`} key={c.code}>
                    <div className="mc-stub">
                      <strong>{c.discountType === 'Percentage' ? `${c.value}%` : money(c.value)}</strong>
                      <span>OFF</span>
                    </div>
                    <div className="mc-body">
                      <div className="mc-code-row">
                        <code className="mc-code">{c.code}</code>
                        {personalCodes.has(c.code) && <span className="mc-tag">Just for you</span>}
                      </div>
                      <p className="mc-desc">{headline(c)}</p>
                      <ul className="mc-meta">
                        <li>{c.minOrderAmount > 0 ? `Valid on orders above ${money(c.minOrderAmount)}` : 'No minimum order'}</li>
                        <li>{c.expiresAt ? `Expires ${fmtDate(c.expiresAt)}` : 'No expiry date'}</li>
                      </ul>
                      <div className="mc-actions">
                        <button type="button" className={`mc-btn ${copied === c.code ? 'is-done' : ''}`} onClick={() => copy(c.code)}>
                          {copied === c.code ? 'Copied!' : 'Copy code'}
                        </button>
                        <Link className="mc-link" to="/checkout">Use at checkout →</Link>
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>

          {/* ---------- redemption history ---------- */}
          <div className="mc-card">
            <div className="mc-card-head">
              <h2>Redemption history {!loading && <span className="mc-count">{history.length}</span>}</h2>
              {history.length > 0 && <span className="mc-saved">You saved {money(totalSaved)} in total</span>}
            </div>

            {history.length === 0 ? (
              <div className="mc-empty">
                <strong>No coupons used yet</strong>
                Apply a coupon at checkout and your savings will show up here.
              </div>
            ) : (
              <div className="mc-table-scroll">
                <table className="mc-table">
                  <thead><tr><th>Coupon</th><th>Order</th><th>You saved</th><th>Date</th></tr></thead>
                  <tbody>
                    {history.map((h, i) => (
                      <tr key={i}>
                        <td><code className="mc-code">{h.code}</code></td>
                        <td><Link className="mc-link" to="/orders">#{h.orderId}</Link></td>
                        <td className="mc-saved-amount">-{money(h.discountAmount)}</td>
                        <td>{fmtDateTime(h.redeemedAt)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>

        </div>
      </div>
    </div>
  )
}