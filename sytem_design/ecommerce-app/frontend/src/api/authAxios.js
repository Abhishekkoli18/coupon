import axios from 'axios'
 
const authAxios = axios.create({
  baseURL: 'https://07fpmmvf-5000.inc1.devtunnels.ms/api',
})
 
export default authAxios