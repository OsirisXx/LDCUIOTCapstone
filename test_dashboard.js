const axios = require('axios');

async function testDashboard() {
    try {
        // First, let's try to login with a test user
        console.log('Testing login...');
        const loginResponse = await axios.post('http://localhost:5000/api/auth/login', {
            email: 'admin@liceo.edu.ph', // Valid admin email
            password: 'password123'
        });
        
        console.log('Login successful!');
        const token = loginResponse.data.token;
        
        // Test dashboard stats
        console.log('Testing dashboard stats...');
        const statsResponse = await axios.get('http://localhost:5000/api/dashboard/stats', {
            headers: {
                'Authorization': `Bearer ${token}`
            }
        });
        
        console.log('Dashboard stats:', JSON.stringify(statsResponse.data, null, 2));
        
        // Test recent activity
        console.log('Testing recent activity...');
        const activityResponse = await axios.get('http://localhost:5000/api/dashboard/recent-activity', {
            headers: {
                'Authorization': `Bearer ${token}`
            }
        });
        
        console.log('Recent activity:', JSON.stringify(activityResponse.data, null, 2));
        
    } catch (error) {
        console.error('Error:', error.response?.data || error.message);
        console.error('Full error:', error.response?.status, error.response?.statusText);
        
        // If login fails, let's try to get a user email from the database
        console.log('Login failed, trying to find a user email...');
        // We'll need to check the database directly
    }
}

testDashboard();
