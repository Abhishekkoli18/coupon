import { useEffect } from 'react'
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import toast from 'react-hot-toast'
import { useAuth } from '../context/AuthContext'

// '/api' -> '/hubs/coupons' in Docker, 'http://localhost:5000/api' -> 'http://localhost:5000/hubs/coupons' in dev
const API = import.meta.env.VITE_API_URL || '/api'
const HUB_URL = API.replace(/\/api$/, '') + '/hubs/coupons'

export default function CouponNotifier() {
  const { user } = useAuth()

  useEffect(() => {
    if (!user) return                                  // connect only while logged in

    const connection = new HubConnectionBuilder()
      .withUrl(HUB_URL, { accessTokenFactory: () => localStorage.getItem('token') || '' })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    connection.on('CouponAvailable', (c) => {
      const off = c.discountType === 'Percentage' ? `${c.value}% off` : `₹${c.value} off`
      toast.success(`New coupon ${c.code}: ${off}`, { duration: 7000 })
      window.dispatchEvent(new Event('coupons-changed'))      // pages showing coupons refresh themselves
    })

    connection.start().catch(() => {})                  // notifications are a bonus: the app works without them

    return () => { connection.stop() }                  // disconnect on logout or unmount
  }, [user])

  return null                                           // draws nothing, it only listens
}