import axios from 'axios'

const authReview = axios.create({
  baseURL: 'https://72vmszcr-5000.inc1.devtunnels.ms/api',
})

// Automatically attach JWT token to every request
authReview.interceptors.request.use(config => {
  const token = localStorage.getItem('token')

  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }

  return config
})

export default authReview